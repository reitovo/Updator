using System.Diagnostics;
using System.Globalization;
using System.Security.AccessControl;
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

void Exec(string cmd) {
   var escapedArgs = cmd.Replace("\"", "\\\"");

   using var process = new Process {
      StartInfo = new ProcessStartInfo {
         RedirectStandardOutput = true,
         UseShellExecute = false,
         CreateNoWindow = true,
         WindowStyle = ProcessWindowStyle.Hidden,
         FileName = "/bin/bash",
         Arguments = $"-c \"{escapedArgs}\""
      }
   };

   process.Start();
   process.WaitForExit();
}

// Set console encoding to utf-8
Console.OutputEncoding = Encoding.UTF8;
AnsiConsole.Profile.Encoding = Encoding.UTF8;

// Print a hint if the program hangs too long
AnsiConsole.MarkupLine(Strings.KillWhenTooLong);

// If the program is started by self-update procedure
if (args.Length == 3) {
   if (args[0] == "self-update") {
      await AnsiConsole.Progress()
         .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(),
            new SpinnerColumn(Spinner.Known.Dots2)).StartAsync(async ctx => {
            try {
               var downloaderUrl = args[2];
               var task = ctx.AddTask(Strings.CheckDownloaderUpdate);
               // We will copy the newer version back to place. Because we are now at a temp path.
               var writeBack = args[1];
               var env = OperatingSystem.IsWindows() ? "win-x64" :
                  OperatingSystem.IsLinux() ? "linux-x64" :
                  OperatingSystem.IsMacOS() ? "osx-x64" : null;
               if (env != null) {
                  using var http = new HttpClient(new SocketsHttpHandler() {
                     ConnectTimeout = TimeSpan.FromSeconds(10)
                  });
                  var signature = await http.GetByteArrayAsync(Path.Combine(downloaderUrl, $"cli-{env}.sha512"));

                  // Download with progress
                  // TODO: Extract this code in a extension.
                  var resp = await http.GetAsync(Path.Combine(downloaderUrl, $"cli-{env}"),
                     HttpCompletionOption.ResponseHeadersRead);
                  var len = resp.Content.Headers.ContentLength!.Value;
                  task.MaxValue = len;

                  var ss = await resp.Content.ReadAsStreamAsync();
                  var ms = new MemoryStream();

                  int bytesRead;
                  var buffer = new byte[81920];
                  while ((bytesRead = await ss.ReadAsync(buffer).ConfigureAwait(false)) != 0) {
                     await ms.WriteAsync(buffer.AsMemory(0, bytesRead));
                     task.Increment(bytesRead);
                  }

                  var payload = ms.ToArray();
                  var hash = SHA512.HashData(payload);

                  // Compare SHA512 checksum
                  if (hash.SequenceEqual(signature)) {
                     var file = Path.GetTempFileName();
                     File.WriteAllBytes(file, payload);

                     // The original process might lock the file, wait it.
                     while (true) {
                        try {
                           File.Move(file, writeBack, true);
                           break;
                        } catch (Exception) {
                           await Task.Delay(TimeSpan.FromSeconds(1));
                           AnsiConsole.MarkupLine(Strings.FailedToMove);
                        }
                     }

                     if (!OperatingSystem.IsWindows()) {
                        Exec($"chmod +x {writeBack}");
                     }

                     // Start the original process
                     Process.Start(new ProcessStartInfo() {
                        FileName = writeBack,
                        CreateNoWindow = false,
                        UseShellExecute = true
                     });
                  } else {
                     throw new InvalidDataException("sha512 not match");
                  }
               }
            } catch (Exception ex) {
               AnsiConsole.WriteException(ex);
               AnsiConsole.MarkupLine(Strings.DonwloaderUpdateFailed);
               AnsiConsole.MarkupLine($"[link]https://reito.fun[/]");
            }
         });
      AnsiConsole.MarkupLine(Strings.SelfUpdateDone);
      await Task.Delay(TimeSpan.FromSeconds(3));
      return;
   }
}

// Default downloader self-update url.
var downloaderUrl = "https://github.com/cnSchwarzer/Updator/releases/latest/download";
var projectName = string.Empty;

// Reads sources.json
var sourcesPath = "./sources.json";
if (!File.Exists(sourcesPath)) {
   sourcesPath = Path.Combine(Directory.GetParent(Environment.ProcessPath!)!.FullName, "sources.json");
   if (!File.Exists(sourcesPath)) {
      AnsiConsole.MarkupLine(Strings.SourcesNotFound);
      return;
   }
}
var sources = await JsonSerializer.DeserializeAsync(new MemoryStream(File.ReadAllBytes(sourcesPath)),
   SourcesSerializer.Default.Sources);

// If there's custom downloader url, replace it
if (!string.IsNullOrWhiteSpace(sources.customDownloaderUrl)) {
   downloaderUrl = sources.customDownloaderUrl;
   Debug.WriteLine($"Using custom downloader url {downloaderUrl}");
}

// Check for downloader update
var latestDownloaderVersion = 0;
await AnsiConsole.Status().Spinner(Spinner.Known.Dots2).StartAsync(Strings.CheckDownloaderUpdate, async ctx => {
   try {
      using var http = new HttpClient(new SocketsHttpHandler() {
         ConnectTimeout = TimeSpan.FromSeconds(3)
      });
      if (int.TryParse(await http.GetStringAsync(Path.Combine(downloaderUrl, "build-id")), out var v)) {
         latestDownloaderVersion = v;
      }
   } catch (Exception ex) {
      AnsiConsole.WriteException(ex);
   }
});

// If there's an update for downloader itself, ask user to make a choice.
if (latestDownloaderVersion > DownloaderMeta.Version) {
   var updateSelf = AnsiConsole.Prompt(new SelectionPrompt<string>()
      .Title(string.Format(Strings.UpdateDownloader, DownloaderMeta.Version, latestDownloaderVersion)).AddChoices(new[] {
         Strings.Yes, Strings.No
      }));
   if (updateSelf == Strings.Yes) {
      // Copy itself to a temp file and run self-update procedure.
      var temp = Path.GetTempFileName() + ".exe";
      var current = Environment.ProcessPath!;
      File.Copy(current, temp, true);
      Process.Start(new ProcessStartInfo() {
         FileName = temp,
         Arguments = $"""self-update "{current}" "{downloaderUrl}" """,
         CreateNoWindow = false,
         UseShellExecute = true
      });
      return;
   }
}

// Update logs to display if there's any
var updateLogs = new List<DistUpdateLog>();

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

await AnsiConsole.Progress()
   .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(),
      new SpinnerColumn(Spinner.Known.Dots2)).StartAsync(async p => {
      var task = p.AddTask(Strings.UpdateSourcesJson);

      // Update sources.json if set.
      if (!string.IsNullOrWhiteSpace(sources.sourcesUrl) && !sources.disableSourcesUpdate) {
         try {
            using var http = new HttpClient(new SocketsHttpHandler() {
               ConnectTimeout = TimeSpan.FromSeconds(10)
            });
            var newSources = await http.GetByteArrayAsync(sources.sourcesUrl);
            task.Increment(80);

            var newSourcesObj = await JsonSerializer.DeserializeAsync(new MemoryStream(newSources),
               SourcesSerializer.Default.Sources);
            task.Increment(15);

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

               sources = newSourcesObj;
               var newSourcesStr = JsonSerializer.Serialize(newSourcesObj, new SourcesSerializer(
                  new JsonSerializerOptions() {
                     WriteIndented = true,
                     DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
                  }).Sources);
               await File.WriteAllTextAsync(sourcesPath, newSourcesStr);
            }
            task.Increment(5);

            task.Description = $"[cyan2]{newSourcesObj.version}[/] " + Strings.UpdatedSourcesJson;
         } catch (Exception ex) {
            AnsiConsole.WriteException(ex);
            task.Value = task.MaxValue;
         }
      } else {
         task.Description = Strings.UpdatedSourcesJson;
         task.Value = task.MaxValue;
      }

      var source = GetSelectedSource(sources);
      if (source == null) {
         AnsiConsole.MarkupLine(Strings.ShouldUniqueSource);
         return;
      }

      DistDescription desc = null;
      task = p.AddTask(Strings.DownloadDescription);

      // Download description file for the distribution.
      try {
         using var http = new HttpClient(new SocketsHttpHandler() {
            ConnectTimeout = TimeSpan.FromSeconds(10)
         });
         var descBytes = await http.GetByteArrayAsync(Path.Combine(source.distributionUrl, "__description.json"));
         task.Increment(80);

         desc = await JsonSerializer.DeserializeAsync(new MemoryStream(descBytes),
            DistDescriptionSerializer.Default.DistDescription);
         task.Increment(20);
         task.Description = $"[cyan2]{desc.buildId}[/] " + Strings.DownloadedDescription;
      } catch (Exception ex) {
         AnsiConsole.WriteException(ex);
         return;
      }

      // Save the project name for further display use.
      projectName = desc.projectName;

      // Print a pretty table for project name
      var table = new Table();
      table.AddColumn("");
      if (desc.projectName.All(char.IsAscii)) {
         table.AddRow(new FigletText(desc.projectName).Centered().Color(Color.RoyalBlue1));
      } else {
         table.AddRow(new Markup($"[royalblue1]{desc.projectName}[/]").Centered());
         table.Expand();
      }
      table.AddRow(new Markup($"[blue]{desc.versionString} ({desc.buildId})[/]").Centered());
      table.HideHeaders();
      table.Border(TableBorder.Rounded);
      AnsiConsole.Write(table);

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
         AnsiConsole.WriteException(new ArgumentNullException($"checksum provider"));
         return;
      }

      task = p.AddTask(Strings.DownloadUpdateFiles);
      var distRoot = new DirectoryInfo(desc.channel).FullName;
      var executable = Path.Combine(distRoot, desc.executable);
      var descPath = Path.Combine(distRoot, "__description.json");

      if (!Directory.Exists(distRoot)) {
         Directory.CreateDirectory(distRoot);
      }

      task.MaxValue = desc.files.Count;

      var sw = new Stopwatch();
      sw.Start();

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
                  var b = await http.GetByteArrayAsync(Path.Combine(source.distributionUrl, f.objectKey));
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
                  AnsiConsole.WriteException(ex);
                  await Task.Delay(TimeSpan.FromSeconds(1), ct);
               }
            }
         }

         task.Increment(1);
      });
      var sec = sw.Elapsed.TotalSeconds;

      task.Description = $"[cyan2]{sec:f2}s[/] " + Strings.DownloadedUpdateFiles;

      // If there's an old description file, and have update logs
      if (File.Exists(descPath) && desc.updateLogs is {Count: > 0}) {
         try {
            var oldDesc = await JsonSerializer.DeserializeAsync(new MemoryStream(File.ReadAllBytes(descPath)),
               DistDescriptionSerializer.Default.DistDescription);
            // Display needed logs
            var logs = desc.updateLogs.Where(a => a.buildId > oldDesc.buildId).ToList();
            logs.Sort((a, b) => b.buildId.CompareTo(a.buildId));
            updateLogs.AddRange(logs);
         } catch (Exception ex) {
            AnsiConsole.WriteException(ex);
         }
      }

      File.WriteAllText(descPath, JsonSerializer.Serialize(desc, DistDescriptionSerializer.Default.DistDescription));

      if (!OperatingSystem.IsWindows()) {
         Exec($"chmod +x {executable}");
      }

      var passArgument = string.Empty;
      if (!string.IsNullOrWhiteSpace(desc.passBuildId)) {
         passArgument += $"--{desc.passBuildId} {desc.buildId} ";
      }

      // Start the payload executable
      Process.Start(new ProcessStartInfo() {
         FileName = executable,
         WorkingDirectory = new DirectoryInfo(executable).Parent!.FullName,
         Arguments = passArgument,
         UseShellExecute = true
      });
   });

// If any printable update logs.
if (updateLogs.Any()) {
   AnsiConsole.MarkupLine(Strings.UpdateLogs);
   foreach (var updateLog in updateLogs) {
      var items = new List<string>();
      if (updateLog.items.TryGetValue(CultureInfo.CurrentCulture.TwoLetterISOLanguageName, out var localized)) {
         items.AddRange(localized);
      } else if (updateLog.items.TryGetValue("_", out var def)) {
         items.AddRange(def);
      }
      var table = new Table();
      table.AddColumn($"[blue]{updateLog.versionString} ({updateLog.buildId})[/]");
      foreach (var i in items) {
         table.AddRow(i);
      }
      AnsiConsole.Write(table);
   }
}

// Print a hint
if (!string.IsNullOrWhiteSpace(projectName)) {
   await AnsiConsole.Status().Spinner(Spinner.Known.Dots2).StartAsync(string.Format(Strings.UpdateDone, projectName),
      async ctx => { await Task.Delay(TimeSpan.FromSeconds(2)); });
}

// If there's log.
if (updateLogs.Any()) {
   await Task.Delay(TimeSpan.FromSeconds(5));
}