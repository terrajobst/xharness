﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.XHarness.Android;
using Microsoft.DotNet.XHarness.CLI.Common;
using Microsoft.Extensions.Logging;
using Mono.Options;

namespace Microsoft.DotNet.XHarness.CLI.Android
{
    internal class AndroidTestCommand : TestCommand
    {
        private readonly AndroidTestCommandArguments _arguments = new AndroidTestCommandArguments();
        protected override ITestCommandArguments TestArguments => _arguments;

        public AndroidTestCommand() : base()
        {
            Options = new OptionSet() {
                "usage: android test [OPTIONS]",
                "",
                "Executes tests on and Android device, waits up to a given timeout, then copies files off the device.",
                { "arg=", "Argument to pass to the instrumentation, in form key=value", v =>
                    {
                        string[] argPair = v.Split('=');

                        if (argPair.Length != 2)
                        {
                            Options.WriteOptionDescriptions(Console.Out);
                            return;
                        }
                        else
                        {
                            _arguments.InstrumentationArguments.Add(argPair[0].Trim(), argPair[1].Trim());
                        }
                    }
                },
                { "instrumentation|i=", "If specified, attempt to run instrumentation with this name instead of the default for the supplied APK.",  v => _arguments.InstrumentationName = v},
            };

            foreach (var option in CommonOptions)
            {
                Options.Add(option);
            }
        }

        protected override Task<int> InvokeInternal()
        {
            _log.LogDebug($"Android Test command called: App = {_arguments.AppPackagePath}{Environment.NewLine}Instrumentation Name = {_arguments.InstrumentationName}");
            _log.LogDebug($"Output Directory:{_arguments.OutputDirectory}{Environment.NewLine}Working Directory = {_arguments.WorkingDirectory}{Environment.NewLine}Timeout = {_arguments.Timeout.TotalSeconds} seconds.");
            _log.LogDebug("Arguments to instrumentation:");

            if (!File.Exists(_arguments.AppPackagePath))
            {
                _log.LogCritical($"Couldn't find {_arguments.AppPackagePath}!");
                return Task.FromResult((int)ExitCodes.PACKAGE_NOT_FOUND);
            }
            var runner = new AdbRunner(_log);
            string apkName = Path.GetFileNameWithoutExtension(_arguments.AppPackagePath);

            try
            {
                using (_log.BeginScope("Initialization and setup of APK on device"))
                {
                    runner.KillAdbServer();
                    runner.StartAdbServer();
                    runner.ClearAdbLog();

                    _log.LogDebug($"Working with {runner.GetAdbVersion()}");

                    // If anything changed about the app, Install will fail; uninstall it first.
                    // (we'll ignore if it's not present)
                    runner.UninstallApk(apkName);
                    if (runner.InstallApk(_arguments.AppPackagePath) != 0)
                    {
                        _log.LogCritical("Install failure: Test command cannot continue");
                        return Task.FromResult((int)ExitCodes.PACKAGE_INSTALLATION_FAILURE);
                    }
                    runner.KillApk(apkName);

                    // App needs to be able to read and write the storage folders we use for IO
                    runner.GrantPermissions(apkName,
                        new string[] {"android.permission.READ_EXTERNAL_STORAGE",
                                      "android.permission.WRITE_EXTERNAL_STORAGE"});
                }

                // No class name = default Instrumentation
                runner.RunApkInstrumentation(apkName, _arguments.InstrumentationName, _arguments.InstrumentationArguments);

                using (_log.BeginScope("Post-test copy and cleanup"))
                {
                    var logs = runner.PullFiles("/sdcard/Documents/helix-results", _arguments.OutputDirectory);
                    foreach (string log in logs)
                    {
                        _log.LogDebug($"Detected output file: {log}");
                    }
                    runner.DumpAdbLog(Path.Combine(_arguments.OutputDirectory, "adb-logcat.log"));
                    runner.UninstallApk(apkName);
                }

                return Task.FromResult((int)ExitCodes.SUCCESS);
            }
            catch (Exception toLog)
            {
                _log.LogCritical(toLog, $"Failure to run test package: {toLog.Message}");
            }
            finally
            {
                runner.KillAdbServer();
            }

            return Task.FromResult((int)ExitCodes.GENERAL_FAILURE);
        }
    }
}
