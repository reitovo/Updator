using Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommandLine;
using Microsoft.Extensions.Logging;
using Updator.Common.Downloader;

namespace Updator.Downloader.UI;

class Program {
   // Initialization code. Don't use any Avalonia, third-party APIs or any
   // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
   // yet and stuff might break. 
   public static void Main(string[] args) {
      try {  
         AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) => {
            App.AppLog.LogError(eventArgs.ExceptionObject as Exception, "未捕获异常");
         };

         App.AppLog.LogInformation($"启动器 {Meta.RuntimeString} {Meta.RuntimeVersion}"); 
         App.AppLog.LogInformation($"工作目录 {Environment.CurrentDirectory}");

         var parsed = new Parser(a => {
            a.AllowMultiInstance = true;
            a.IgnoreUnknownArguments = true;
         }).ParseArguments<Options>(args);
         if (parsed.Value != null) {
            var val = parsed.Value;
            try {
               if (!string.IsNullOrWhiteSpace(val.DeleteFile)) {
                  Task.Run(async () => {
                     var deleteRetry = 5;
                     retryDelete:
                     try {
                        await Task.Delay(TimeSpan.FromSeconds(1));
                        File.Delete(val.DeleteFile);
                     } catch (Exception ex) {
                        Debug.WriteLine(ex);
                        if (deleteRetry-- > 0) {
                           goto retryDelete;
                        }
                     }
                  });
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
   [Option("delete", Required = false, Hidden = true)]
   public string DeleteFile { get; set; }
}