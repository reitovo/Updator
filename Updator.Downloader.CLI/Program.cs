using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;
using Updator.Common;
using Updator.Common.ChecksumProvider;
using Updator.Common.CompressionProvider;
using Updator.Downloader;
using Updator.Downloader.CLI;

var downloaderVersion = 1;
var downloaderUrl = "";

var configPath = "./sources.json";
if (!File.Exists(configPath)) {
   AnsiConsole.MarkupLine(Strings.SourcesNotFound);
   return;
}

var projectName = string.Empty;

await AnsiConsole.Progress()
   .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(),
      new SpinnerColumn(Spinner.Known.Earth)).StartAsync(async p => {
      var sources = await JsonSerializer.DeserializeAsync(new MemoryStream(File.ReadAllBytes(configPath)),
         SourcesSerializer.Default.Sources);

      var task = p.AddTask(Strings.UpdateSourcesJson);
      if (!string.IsNullOrWhiteSpace(sources.sourcesUrl)) {
         try {
            using var http = new HttpClient();
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

      await Parallel.ForEachAsync(desc.files, async (f, _) => {
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
            await using var fs = fullPath.OpenRead();
            await compress.Compress(fs, ms);
            ms.Position = 0;
            var checksum = await check.CalculateChecksum(ms);
            if (checksum != f.checksum) {
               download = true;
            }
         }

         if (download) {
            using var http = new HttpClient();
            await using var sr = await http.GetStreamAsync(Path.Combine(source.distributionUrl, f.objectKey));
            await using var fs = fullPath.OpenWrite();
            await compress.Decompress(sr, fs);
         }

         task.Increment(1);
      });

      task.Description = Strings.DownloadedUpdateFiles;

      Process.Start(new ProcessStartInfo() {
         FileName = executable,
         WorkingDirectory = new FileInfo(executable).Directory!.FullName
      });
   });

await AnsiConsole.Status().Spinner(Spinner.Known.Earth).StartAsync(string.Format(Strings.UpdateDone, projectName),
   async ctx => { await Task.Delay(TimeSpan.FromSeconds(3)); });

await AnsiConsole.Status().Spinner(Spinner.Known.Earth).StartAsync(Strings.CheckDownloaderUpdate, async ctx => {
   try {
      using var http = new HttpClient();

      var updateSelf = AnsiConsole.Prompt(new SelectionPrompt<string>().Title(Strings.UpdateDownloader).PageSize(10)
         .AddChoices(new[] {
            "Apple", "Apricot"
         }));
   } catch (Exception ex) {
      AnsiConsole.WriteException(ex);
   }
});