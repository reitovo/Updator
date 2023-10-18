using Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommandLine;

namespace Updator.Downloader.UI;

class Program {
   // Initialization code. Don't use any Avalonia, third-party APIs or any
   // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
   // yet and stuff might break.
   [STAThread]
   public static void Main(string[] args) {
      var processModule = Process.GetCurrentProcess().MainModule;
      if (processModule != null) {
         var pwd = Path.GetDirectoryName(processModule.FileName);
         if (!string.IsNullOrWhiteSpace(pwd))
            Environment.CurrentDirectory = pwd;
      }

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

      BuildAvaloniaApp()
         .StartWithClassicDesktopLifetime(args);
   }

   // Avalonia configuration, don't remove; also used by visual designer.
   public static AppBuilder BuildAvaloniaApp()
      => AppBuilder.Configure<App>()
         .UsePlatformDetect()
         .LogToTrace();
}

file class Options {
   [Option("delete", Required = false, Hidden = true)]
   public string DeleteFile { get; set; }
}
