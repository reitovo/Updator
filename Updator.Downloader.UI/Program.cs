﻿using Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Microsoft.Extensions.Logging;
using Updator.Common.Downloader;

namespace Updator.Downloader.UI;

class Program {
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break. 
    [STAThread]
    public static void Main(string[] args) {
        try {
            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) => { App.AppLog.LogError(eventArgs.ExceptionObject as Exception, "未捕获异常"); };

            App.AppLog.LogInformation($"启动器 {Meta.RuntimeString} {Meta.RuntimeVersion}");

            var parsed = new Parser(a => {
                a.AllowMultiInstance = true;
                a.IgnoreUnknownArguments = true;
            }).ParseArguments<Options>(args);
            if (parsed.Value != null) {
                var val = parsed.Value;
                try {
                    if (!string.IsNullOrWhiteSpace(val.DeletePath)) {
                        App.AppLog.LogInformation($"启动器自更新 删除：{val.DeletePath}");
                        Task.Run(async () => {
                            var deleteRetry = 5;
                            retryDelete:
                            try {
                                await Task.Delay(TimeSpan.FromSeconds(1));
                                var attr = File.GetAttributes(val.DeletePath);
                                if ((attr & FileAttributes.Directory) == FileAttributes.Directory) {
                                    Directory.Delete(val.DeletePath, true);
                                } else {
                                    File.Delete(val.DeletePath);
                                }
                            } catch (Exception ex) {
                                Debug.WriteLine(ex);
                                if (deleteRetry-- > 0) {
                                    goto retryDelete;
                                }
                            }
                        });
                    }

                    if (val.UpdateSelf) {
                        App.AppLog.LogInformation($"启动器自更新 {val.ProgramName} 路径：{Environment.ProcessPath} 回写：{val.ProgramPath}");
                        var workingDirectory = Directory.GetParent(val.ProgramPath)!.FullName;
                        var writeBackPath = Path.Combine(workingDirectory, val.ProgramName) + Path.GetExtension(val.ProgramPath);
                        if (val.ProgramPath != writeBackPath && File.Exists(val.ProgramPath)) {
                            Retry.Run(() => { File.Delete(val.ProgramPath); }, 10, TimeSpan.FromSeconds(1));
                        }

                        Retry.Run(() => { File.Copy(Environment.ProcessPath!, writeBackPath, true); }, 10, TimeSpan.FromSeconds(1));
                        Process.Start(new ProcessStartInfo() {
                            FileName = writeBackPath,
                            WorkingDirectory = workingDirectory,
                            CreateNoWindow = false,
                            UseShellExecute = true
                        });

                        return;
                    }
                } catch (Exception ex) {
                    Debug.WriteLine(ex);
                }
            }

            App.AppLog.LogInformation($"启动 UI");

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        } catch (Exception ex) {
            App.AppLog.LogError(ex, "错误");
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<AppUI>()
            .UsePlatformDetect()
            .LogToTrace();
}

file class Options {
    [Option("updateSelf", Required = false, Hidden = true)]
    public bool UpdateSelf { get; set; }

    [Option("programPath", Required = false, Hidden = true)]
    public string ProgramPath { get; set; }

    [Option("programName", Required = false, Hidden = true)]
    public string ProgramName { get; set; }

    [Option("delete", Required = false, Hidden = true)]
    public string DeletePath { get; set; }
}

static class Retry {
    public static void Run(Action action, int times, TimeSpan interval) {
        var count = 0;
        while (count < times) {
            try {
                action();
                return;
            } catch (Exception ex) {
                App.AppLog.LogError(ex, "RetryRun");
                count++;
                Thread.Sleep(interval);
            }
        }
    }
}