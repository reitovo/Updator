using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Updator.Downloader.UI;

public partial class AppUI : Application {
   public override void Initialize() {
      AvaloniaXamlLoader.Load(this);
   }

   public override void OnFrameworkInitializationCompleted() {
      if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
         desktop.MainWindow = new MainWindow();
      }

      base.OnFrameworkInitializationCompleted();
   }
}
