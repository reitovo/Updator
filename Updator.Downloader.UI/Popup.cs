using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Models;
using Updator.Common.Downloader;

namespace Updator.Downloader.UI;

public static class Popup { 
   public static void Exception(string error, Exception ex = null) {
      App.AppLog.LogError(ex, error);
      var msg = error + (ex == null ? string.Empty : "\n" + ex);
      Dispatcher.UIThread.Invoke(async () => {
         await MessageBoxManager.GetMessageBoxCustom(new MessageBoxCustomParams() {
               ContentTitle = Strings.Error,
               ContentMessage = msg,
               ButtonDefinitions = new ButtonDefinition[] {
                  new() {
                     Name = Strings.Yes
                  }
               },
               FontFamily = App.FontFamily,
               WindowStartupLocation = WindowStartupLocation.CenterScreen
            })
            .ShowAsync();
         Dispatcher.UIThread.InvokeShutdown();
      });
   }
}
