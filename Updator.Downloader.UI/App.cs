using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
namespace Updator.Downloader.UI;

public static class App {
   public static string AppLocalDataFolder =>
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reito", "Updator");

   static App() {
      Directory.CreateDirectory(AppLocalDataFolder);
   }

   public static ILoggerFactory LogFactory { get; } =
      LoggerFactory.Create(builder => {
         builder.SetMinimumLevel(LogLevel.Trace);
         builder.AddFilter((key, level) => {
            if (key != null) {
               if (key.StartsWith("Microsoft.EntityFrameworkCore") && level < LogLevel.Warning)
                  return false;
               if (key.StartsWith("Microsoft.AspNetCore") && level < LogLevel.Warning)
                  return false;
            }
            return true;
         });
         builder.AddFile("updator.log", levelOverrides: new Dictionary<string, LogLevel>() {
            { "Microsoft.EntityFrameworkCore", LogLevel.Warning },
            { "Microsoft.AspNetCore", LogLevel.Warning },
         }, minimumLevel: LogLevel.Trace, retainedFileCountLimit: 3, fileSizeLimitBytes: 1024 * 1024 * 12);
      });

   public static ILogger AppLog { get; } = LogFactory.CreateLogger("App");
}
