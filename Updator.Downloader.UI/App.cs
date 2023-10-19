using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Avalonia.Media;
using Microsoft.Extensions.Logging;

namespace Updator.Downloader.UI;

public static class App {
   public static string AppLocalDataFolder =>
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reito", "Updator");

   static App() { 
      Directory.CreateDirectory(AppLocalDataFolder);
      LogFactory = LoggerFactory.Create(builder => {
         builder.AddSimpleConsole(o => {
            o.SingleLine = true;
         });
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
         builder.AddFile(Path.Combine(AppLocalDataFolder, "updator.log"), levelOverrides: new Dictionary<string, LogLevel>() {
            {
               "Microsoft.EntityFrameworkCore", LogLevel.Warning
            }, {
               "Microsoft.AspNetCore", LogLevel.Warning
            },
         }, minimumLevel: LogLevel.Trace, retainedFileCountLimit: 3, fileSizeLimitBytes: 1024 * 1024 * 12);
      });
      AppLog = LogFactory.CreateLogger("App");
   }

   private static ILoggerFactory LogFactory { get; }

   public static ILogger AppLog { get; }
   public static FontFamily FontFamily => "PingFang SC, Source Han Sans SC VF, Source Han Sans SC, 等线, 微软雅黑";
}