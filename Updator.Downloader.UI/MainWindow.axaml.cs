using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AsyncImageLoader.Loaders;
using Avalonia.Controls;
using Avalonia.Threading;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;
using Updator.Common;
using Updator.Common.ChecksumProvider;
using Updator.Common.CompressionProvider;
using Updator.Common.Downloader;
using Updator.Downloader.CLI;

namespace Updator.Downloader.UI;

public partial class MainWindow : Window {
   public MainWindow() {
      InitializeComponent();

      AppIcon.Loader = new DiskCachedWebImageLoader();

      // Reads sources.json
      var sourcesPath = "./sources.json";
      if (!File.Exists(sourcesPath)) {
         sourcesPath = Path.Combine(Directory.GetParent(Environment.ProcessPath!)!.FullName, "sources.json");
         if (!File.Exists(sourcesPath)) {
            Popup.Exception(Strings.SourcesNotFound);
            return;
         }
      }

      var sources = JsonSerializer.Deserialize(new MemoryStream(File.ReadAllBytes(sourcesPath)),
         SourcesSerializer.Default.Sources);
      if (sources == null) {
         Popup.Exception(Strings.SourcesNotFound);
         return;
      }

      SetProjectName(sources.defaultName);
      if (!string.IsNullOrWhiteSpace(sources.defaultIcon)) {
         AppIcon.Source = sources.defaultIcon;
      } else {
         AppIcon.Source = "Icon.ico";
      }

      InitializeSource(sourcesPath, sources);
   }

   private void Exec(string cmd) {
      var escapedArgs = cmd.Replace("\"", "\\\"");

      using var process = new Process();
      process.StartInfo = new ProcessStartInfo {
         RedirectStandardOutput = true,
         CreateNoWindow = true,
         WindowStyle = ProcessWindowStyle.Hidden,
         FileName = "/bin/bash",
         Arguments = $"-c \"{escapedArgs}\""
      };

      process.Start();
      process.WaitForExit();
   }

   private void SetProjectName(string name) {
      if (string.IsNullOrWhiteSpace(name))
         return;

      Dispatcher.UIThread.Invoke(() => {
         AppName.Content = name;
      });
   }

   private void SetAppIcon(string icon) {
      if (string.IsNullOrWhiteSpace(icon))
         return;

      if (AppIcon.Source != icon) {
         Dispatcher.UIThread.Invoke(() => {
            AppIcon.Source = icon;
         });
      }
   }

   public async void InitializeSource(string sourcesPath, Sources sources) {
      // Default downloader self-update url.
      var downloaderUrl = "https://github.com/cnSchwarzer/Updator/releases/latest/download";

      // If there's custom downloader url, replace it
      if (!string.IsNullOrWhiteSpace(sources.customDownloaderUrl)) {
         downloaderUrl = sources.customDownloaderUrl;
         Debug.WriteLine($"Using custom downloader url {downloaderUrl}");
      }

      var latestDownloaderVersion = 0;
      try {
         using var http = new HttpClient(new SocketsHttpHandler() {
            ConnectTimeout = TimeSpan.FromSeconds(3)
         });
         if (int.TryParse(await http.GetStringAsync(Path.Combine(downloaderUrl, "build-id")), out var v)) {
            latestDownloaderVersion = v;
         }
      } catch (Exception ex) {
         Popup.Exception(Strings.RequestFailed, ex);
      }

      if (latestDownloaderVersion > Meta.Version) {
         var result = await MessageBoxManager.GetMessageBoxCustom(new MessageBoxCustomParams() {
               ContentTitle = Strings.Update,
               ContentMessage = string.Format(Strings.UpdateDownloaderAsk, Meta.Version, latestDownloaderVersion),
               ButtonDefinitions = new ButtonDefinition[] {
                  new() {
                     Name = Strings.Yes
                  },
                  new() {
                     Name = Strings.No
                  }
               },
               FontFamily = "Source Han Sans SC VF, Source Han Sans SC, 等线, 微软雅黑",
               WindowStartupLocation = WindowStartupLocation.CenterScreen
            })
            .ShowAsync();

         if (result == Strings.Yes) {
            var env = OperatingSystem.IsWindows() ? "win-x64" :
               OperatingSystem.IsLinux() ? "linux-x64" :
               OperatingSystem.IsMacOS() ? "osx-x64" : null;

            SetJobName(Strings.UpdateDownloader);

            using var http = new HttpClient(new SocketsHttpHandler() {
               ConnectTimeout = TimeSpan.FromSeconds(10)
            });
            var signature = await http.GetByteArrayAsync(Path.Combine(downloaderUrl, $"cli-{env}.sha512"));

            // Download with progress
            // TODO: Extract this code in a extension.
            var resp = await http.GetAsync(Path.Combine(downloaderUrl, $"brotli-cli-{env}"),
               HttpCompletionOption.ResponseHeadersRead);
            var len = resp.Content.Headers.ContentLength!.Value;
            JobProgress.Maximum = len;

            var ss = await resp.Content.ReadAsStreamAsync();
            var ms = new MemoryStream();

            int bytesRead;
            var buffer = new byte[81920];
            while ((bytesRead = await ss.ReadAsync(buffer).ConfigureAwait(false)) != 0) {
               await ms.WriteAsync(buffer.AsMemory(0, bytesRead));
               IncrementProgressBar(bytesRead);
            }

            var payload = ms.ToArray();

            var brotli = new Brotli();
            using var compressed = new MemoryStream(payload);
            using var decompressed = new MemoryStream();
            await brotli.Decompress(compressed, decompressed);
            decompressed.Position = 0;
            payload = decompressed.ToArray();

            var hash = SHA512.HashData(payload);

            // Compare SHA512 checksum
            if (hash.SequenceEqual(signature)) {
               var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath)!;
               var ext = Path.GetExtension(Environment.ProcessPath)!;

               name = Regex.Replace(name, @"\(\d+\)", string.Empty);

               var file = $"{name}({latestDownloaderVersion}){ext}";
               await File.WriteAllBytesAsync(file, payload);

               if (!OperatingSystem.IsWindows()) {
                  Exec($"chmod +x {file}");
               }

               Process.Start(new ProcessStartInfo() {
                  FileName = file,
                  CreateNoWindow = false,
                  Arguments = $"--delete \"{Environment.ProcessPath}\""
               });
               Dispatcher.UIThread.InvokeShutdown();
            } else {
               Popup.Exception("更新校验失败");
            }
            return;
         }
      }

      Source GetSelectedSource(Sources srcs) {
         // Select the unique enabled source
         var sourceCandidate = srcs.sources.Where(a => a.enable).ToList();
         if (sourceCandidate.Count != 1) {
            if (string.IsNullOrWhiteSpace(srcs.defaultSourceId))
               return null;
            return srcs.sources.FirstOrDefault(a => a.id == srcs.defaultSourceId);
         }
         return sourceCandidate.First();
      }

      void SetProgressBar(double value) {
         Dispatcher.UIThread.Invoke(() => {
            JobProgress.Value = value;
         });
      }

      void SetJobName(string name) {
         Dispatcher.UIThread.Invoke(() => {
            JobName.Content = name;
         });
      }

      // Update sources.json if set.
      if (!string.IsNullOrWhiteSpace(sources.sourcesUrl) && !sources.disableSourcesUpdate) {
         try {
            SetJobName(Strings.UpdateSourcesJson);

            using var http = new HttpClient(new SocketsHttpHandler() {
               ConnectTimeout = TimeSpan.FromSeconds(10)
            });
            var newSources = await http.GetByteArrayAsync(sources.sourcesUrl);
            SetProgressBar(80);

            var newSourcesObj = await JsonSerializer.DeserializeAsync(new MemoryStream(newSources),
               SourcesSerializer.Default.Sources);
            SetProgressBar(95);

            // Replace the file if the remote one is newer.
            if (newSourcesObj.version > sources.version) {
               // If user changed source, try keep it.
               var defaultId = sources.defaultSourceId;
               if (!string.IsNullOrWhiteSpace(defaultId)) {
                  var s = GetSelectedSource(sources);
                  if (!string.IsNullOrWhiteSpace(s.id) && s.id != defaultId) {
                     var ns = newSourcesObj.sources.FirstOrDefault(a => a.distributionUrl == s.distributionUrl);
                     if (ns != null) {
                        newSourcesObj.sources.ForEach(a => a.enable = false);
                        ns.enable = true;
                     }
                  }
               }

               // If no enabled from new, use current
               if (newSourcesObj.sources.All(a => !a.enable)) {
                  var s = GetSelectedSource(sources);
                  var ns = newSourcesObj.sources.FirstOrDefault(a => a.distributionUrl == s.distributionUrl);
                  if (ns != null) {
                     newSourcesObj.sources.ForEach(a => a.enable = false);
                     ns.enable = true;
                  }
               }

               sources = newSourcesObj;
               var newSourcesStr = JsonSerializer.Serialize(newSourcesObj, new SourcesSerializer(
                  new JsonSerializerOptions() {
                     WriteIndented = true,
                     DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
                  }).Sources);
               await File.WriteAllTextAsync(sourcesPath, newSourcesStr);
            }
            SetProgressBar(100);
         } catch (Exception ex) {
            Popup.Exception(Strings.RequestFailed, ex);
            return;
         }
      }

      var source = GetSelectedSource(sources);
      if (source == null) {
         Popup.Exception(Strings.ShouldUniqueSource);
         return;
      }

      SetProjectName(source.defaultName);
      SetAppIcon(sources.defaultIcon);

      DistDescription desc;
      SetJobName(Strings.DownloadDescription);

      // Download description file for the distribution.
      try {
         using var http = new HttpClient(new SocketsHttpHandler() {
            ConnectTimeout = TimeSpan.FromSeconds(10)
         });
         var descBytes = await http.GetByteArrayAsync(Path.Combine(source.distributionUrl, "__description.json"));
         SetProgressBar(80);

         desc = await JsonSerializer.DeserializeAsync(new MemoryStream(descBytes),
            DistDescriptionSerializer.Default.DistDescription);
         SetProgressBar(100);
      } catch (Exception ex) {
         Popup.Exception(Strings.RequestFailed, ex);
         return;
      }

      SetProjectName(desc.projectName);
      Dispatcher.UIThread.Invoke(() => {
         AppVersion.Content = $"{desc.versionString} ({desc.buildId})";
         SetAppIcon(desc.appIconUrl);
      });

      // Restore compression provider
      ICompressionProvider compress = desc.compression switch {
         "brotli" => new Brotli(),
         "gzip" => new GZip(),
         _ => new Raw()
      };

      // Restore checksum provider
      IChecksumProvider check = desc.checksum switch {
         "crc64" => new Crc64(),
         _ => null
      };

      if (check == null) {
         Popup.Exception(Strings.RequestFailed);
         return;
      }

      SetJobName(Strings.DownloadUpdateFiles);
      var distRoot = new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, desc.channel)).FullName;
      var executable = Path.Combine(distRoot, desc.executable);
      var descPath = Path.Combine(distRoot, "__description.json");

      if (!Directory.Exists(distRoot)) {
         Directory.CreateDirectory(distRoot);
      }

      JobProgress.Maximum = desc.files.Count;

      var displayUpdateLog = false;

      // If there's an old description file, and have update logs
      if (File.Exists(descPath)) {
         try {
            var oldDesc = await JsonSerializer.DeserializeAsync(new MemoryStream(File.ReadAllBytes(descPath)),
               DistDescriptionSerializer.Default.DistDescription);

            // If any of the reinstall build id is larger than current 
            if (desc.reinstallBuildId is { Count: > 0 }) {
               foreach (var id in desc.reinstallBuildId) {
                  if (oldDesc.buildId < id) {
                     Directory.Delete(distRoot, true);
                     break;
                  }
               }
            }


            if (desc.updateLogs is { Count: > 0 }) {
               var logs = desc.updateLogs.Where(a => a.buildId > oldDesc.buildId).ToList();
               if (logs.Any()) {
                  displayUpdateLog = true;
               }
            }
         } catch (Exception ex) {
            Popup.Exception(Strings.RequestFailed, ex);
         }
      }

      void IncrementProgressBar(double value) {
         Dispatcher.UIThread.Invoke(() => {
            JobProgress.Value += value;
         });
      }

      // Compare checksum and download if mismatch.
      // Use parallel to speed up.
      await Parallel.ForEachAsync(desc.files, async (f, ct) => {
         var fullPath = new FileInfo(Path.Combine(distRoot, f.objectKey));
         var dir = fullPath.Directory!;
         if (!dir.Exists) {
            Directory.CreateDirectory(dir.FullName);
         }

         var download = false;
         if (!fullPath.Exists) {
            download = true;
         } else {
            using var ms = new MemoryStream();
            var fs = fullPath.OpenRead();
            await compress.Compress(fs, ms);
            await fs.DisposeAsync();
            fs.Close();
            ms.Position = 0;
            var checksum = await check.CalculateChecksum(ms);
            if (checksum != f.checksum) {
               download = true;
            }
         }

         // Download if needed
         if (download) {
            while (true) {
               try {
                  using var http = new HttpClient(new SocketsHttpHandler() {
                     ConnectTimeout = TimeSpan.FromSeconds(10),
                  });
                  var b = await http.GetByteArrayAsync(Path.Combine(source.distributionUrl, f.objectKey), ct);
                  using var ms = new MemoryStream(b);
                  ms.Position = 0;
                  var c = await check.CalculateChecksum(ms);
                  if (c != f.checksum) {
                     throw new InvalidDataException("checksum failed");
                  }
                  ms.Position = 0;
                  if (fullPath.Exists)
                     fullPath.Delete();
                  var fs = fullPath.Create();
                  await compress.Decompress(ms, fs);
                  await fs.DisposeAsync();
                  fs.Close();
                  break;
               } catch (TaskCanceledException) {
                  // ignored
               } catch (Exception ex) {
                  Debug.WriteLine(ex);
                  await Task.Delay(TimeSpan.FromSeconds(1), ct);
               }
            }
         }

         IncrementProgressBar(1);
      });

      await File.WriteAllTextAsync(descPath, JsonSerializer.Serialize(desc, DistDescriptionSerializer.Default.DistDescription));

      var passArgument = string.Empty;
      if (!string.IsNullOrWhiteSpace(desc.passBuildId)) {
         passArgument += $"--{desc.passBuildId} {desc.buildId} ";
      }

      SetJobName(string.Format(Strings.UpdateDone, desc.projectName));

      if (Design.IsDesignMode) {
         return;
      }

      // Start the payload executable 
      Process.Start(new ProcessStartInfo() {
         FileName = executable,
         WorkingDirectory = new DirectoryInfo(executable).Parent!.FullName,
         Arguments = passArgument,
         UseShellExecute = true
      });

      if (displayUpdateLog && !string.IsNullOrWhiteSpace(desc.updateLogUrl)) {
         var result = await MessageBoxManager.GetMessageBoxCustom(new MessageBoxCustomParams() {
               ContentTitle = Strings.Update,
               ContentMessage = Strings.ShowUpdateLog,
               ButtonDefinitions = new ButtonDefinition[] {
                  new() {
                     Name = Strings.Yes
                  },
                  new() {
                     Name = Strings.No
                  }
               },
               FontFamily = "Source Han Sans SC VF, Source Han Sans SC, 等线, 微软雅黑",
               WindowStartupLocation = WindowStartupLocation.CenterScreen
            })
            .ShowAsync();

         if (result == Strings.Yes) {
            Process.Start(new ProcessStartInfo(desc.updateLogUrl) { UseShellExecute = true });
         }
      }

      await Task.Delay(TimeSpan.FromSeconds(3));
      Dispatcher.UIThread.InvokeShutdown();
   }
}
