using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;
using Updator.Common;
using Updator.Common.ChecksumProvider;
using Updator.Common.CompressionProvider;
using Updator.Downloader;
using Updator.Downloader.CLI;

Console.OutputEncoding = Encoding.UTF8;
AnsiConsole.Profile.Encoding = Encoding.UTF8;

var downloaderVersion = 1;
var downloaderUrl = "https://github.com/cnSchwarzer/Updator/releases/latest/download";
var projectName = string.Empty;

var configPath = "./sources.json";
if (!File.Exists(configPath)) {
   AnsiConsole.MarkupLine(Strings.SourcesNotFound);
   return;
}
var sources = await JsonSerializer.DeserializeAsync(new MemoryStream(File.ReadAllBytes(configPath)),
   SourcesSerializer.Default.Sources);

if (!string.IsNullOrWhiteSpace(sources.customDownloaderUrl)) {
   downloaderUrl = sources.customDownloaderUrl;
   Debug.WriteLine($"Using custom downloader url {downloaderUrl}");
}

if (args.Length == 2) {
   if (args[0] == "self-update") {
      await AnsiConsole.Status().Spinner(Spinner.Known.Dots2).StartAsync(Strings.CheckDownloaderUpdate, async ctx => {
         try {
            var writeBack = args[1];
            var env = OperatingSystem.IsWindows() ? "win-x64" :
               OperatingSystem.IsLinux() ? "linux-x64" :
               OperatingSystem.IsMacOS() ? "osx-x64" : null;
            if (env != null) {
               using var http = new HttpClient();
               http.Timeout = TimeSpan.FromSeconds(5);
               var payload = await http.GetByteArrayAsync(Path.Combine(downloaderUrl, $"cli-{env}"));
               var signature = await http.GetByteArrayAsync(Path.Combine(downloaderUrl, $"cli-{env}.sha512"));
               var hash = SHA512.HashData(payload);
               if (hash.SequenceEqual(signature)) {
                  var file = Path.GetTempFileName();
                  File.WriteAllBytes(file, payload);

                  while (true) {
                     try {
                        File.Move(file, writeBack, true);
                        break;
                     } catch (Exception) {
                        await Task.Delay(TimeSpan.FromSeconds(1));
                     }
                  }
               }
            }
         } catch (Exception ex) {
            AnsiConsole.WriteException(ex);
         }
      });
      AnsiConsole.MarkupLine(Strings.SelfUpdateDone);
      await Task.Delay(TimeSpan.FromSeconds(3));
      return;
   }
}

await AnsiConsole.Progress()
   .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(),
      new SpinnerColumn(Spinner.Known.Dots2)).StartAsync(async p => {
      var task = p.AddTask(Strings.UpdateSourcesJson);
      if (!string.IsNullOrWhiteSpace(sources.sourcesUrl)) {
         try {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            var newSources = await http.GetByteArrayAsync(sources.sourcesUrl);
            task.Increment(80);

            var newSourcesObj = await JsonSerializer.DeserializeAsync(new MemoryStream(newSources),
               SourcesSerializer.Default.Sources);
            task.Increment(15);

            if (newSourcesObj.version > sources.version) {
               await File.WriteAllBytesAsync(configPath, newSources);
               sources = newSourcesObj;
            }
            task.Increment(5);

            task.Description = Strings.UpdatedSourcesJson;
         } catch (Exception ex) {
            AnsiConsole.WriteException(ex);
            return;
         }
      } else {
         task.Description = Strings.UpdatedSourcesJson;
      }

      var sourceCandidate = sources.sources.Where(a => a.enable).ToList();
      if (sourceCandidate.Count != 1) {
         AnsiConsole.MarkupLine(Strings.ShouldUniqueSource);
         return;
      }

      var source = sourceCandidate.First();
      DistDescription desc = null;
      task = p.AddTask(Strings.DownloadDescription);

      try {
         using var http = new HttpClient();
         http.Timeout = TimeSpan.FromSeconds(1);
         var descBytes = await http.GetByteArrayAsync(Path.Combine(source.distributionUrl, "__description.json"));
         task.Increment(80);

         desc = await JsonSerializer.DeserializeAsync(new MemoryStream(descBytes),
            DistDescriptionSerializer.Default.DistDescription);
         task.Increment(20);
         task.Description = Strings.DownloadedDescription;
      } catch (Exception ex) {
         AnsiConsole.WriteException(ex);
         return;
      }

      projectName = desc.projectName;

      var table = new Table();
      table.AddColumn("");
      table.AddRow(new FigletText(desc.projectName).Centered().Color(Color.RoyalBlue1));
      table.AddRow(new Markup($"[blue]{desc.versionString} ({desc.buildId})[/]").Centered());
      table.Collapse();
      table.HideHeaders();
      table.Border(TableBorder.Rounded);
      AnsiConsole.Write(table);

      ICompressionProvider compress = desc.compression switch {
         "brotli" => new Brotli(),
         "gzip" => new GZip(),
         _ => new Raw()
      };

      IChecksumProvider check = desc.checksum switch {
         "crc64" => new Crc64(),
         _ => null
      };
      if (check == null) {
         AnsiConsole.WriteException(new ArgumentNullException($"checksum provider"));
         return;
      }

      task = p.AddTask(Strings.DownloadUpdateFiles);
      var distRoot = new DirectoryInfo(desc.channel).FullName;
      var executable = Path.Combine(distRoot, desc.executable);

      if (!Directory.Exists(distRoot)) {
         Directory.CreateDirectory(distRoot);
      }

      task.MaxValue = desc.files.Count;

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
            ms.Position = 0;
            var checksum = await check.CalculateChecksum(ms);
            if (checksum != f.checksum) {
               download = true;
            }
            await fs.DisposeAsync();
            fs.Close();
         }

         if (download) {
            while (true) {
               try {
                  using var http = new HttpClient();
                  http.Timeout = TimeSpan.FromSeconds(5);
                  var b = await http.GetByteArrayAsync(Path.Combine(source.distributionUrl, f.objectKey));
                  using var ms = new MemoryStream(b);
                  ms.Position = 0;
                  var c = await check.CalculateChecksum(ms);
                  if (c != f.checksum) {
                     throw new InvalidDataException("checksum failed");
                  }
                  ms.Position = 0;
                  var fs = fullPath.OpenWrite();
                  await compress.Decompress(ms, fs);
                  await fs.DisposeAsync();
                  fs.Close();
                  break;
               } catch (Exception ex) {
                  AnsiConsole.WriteException(ex);
                  await Task.Delay(TimeSpan.FromSeconds(1), ct);
               }
            }
         }

         task.Increment(1);
      });

      task.Description = Strings.DownloadedUpdateFiles;

      Process.Start(new ProcessStartInfo() {
         FileName = executable,
         WorkingDirectory = new FileInfo(executable).Directory!.FullName
      });
   });

if (!string.IsNullOrWhiteSpace(projectName)) {
   await AnsiConsole.Status().Spinner(Spinner.Known.Dots2).StartAsync(string.Format(Strings.UpdateDone, projectName),
      async ctx => { await Task.Delay(TimeSpan.FromSeconds(3)); });
}

var latestDownloaderVersion = 0;
await AnsiConsole.Status().Spinner(Spinner.Known.Dots2).StartAsync(Strings.CheckDownloaderUpdate, async ctx => {
   try {
      using var http = new HttpClient();
      if (int.TryParse(await http.GetStringAsync(Path.Combine(downloaderUrl, "build-id")), out var v)) {
         latestDownloaderVersion = v;
      }
   } catch (Exception ex) {
      AnsiConsole.WriteException(ex);
   }
});

if (latestDownloaderVersion > downloaderVersion) {
   var updateSelf = AnsiConsole.Prompt(new SelectionPrompt<string>()
      .Title(string.Format(Strings.UpdateDownloader, latestDownloaderVersion)).AddChoices(new[] {
         Strings.Yes, Strings.No
      }));
   if (updateSelf == Strings.Yes) {
      var temp = Path.GetTempFileName() + ".exe";
      var current = Environment.ProcessPath!;
      File.Copy(current, temp, true);
      Process.Start(new ProcessStartInfo() {
         FileName = temp,
         Arguments = $"""self-update "{current}" """,
         CreateNoWindow = false,
         UseShellExecute = true
      });
   }
}