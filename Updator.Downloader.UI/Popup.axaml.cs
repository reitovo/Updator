using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace Updator.Downloader.UI;

public partial class Popup : Window {
   public Popup() {
      InitializeComponent();
   }

   public void Initialize(string title, string message, string detail) {
      Title = title;
      TitleText.Content = title;
      Summary.Content = message;
      Detail.Content = detail;
   }

   public static void Exception(string error, Exception ex = null) {
      App.AppLog.LogError(ex, error);
      Dispatcher.UIThread.Invoke(() => {
         var window = new Popup();
         window.Initialize(Strings.Error, error, ex?.ToString());
         window.Closed += (sender, args) => {
            Dispatcher.UIThread.InvokeShutdown();
         };
         window.Show();
      });
   }
}
