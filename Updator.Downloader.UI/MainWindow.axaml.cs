using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AsyncImageLoader.Loaders;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;
using Updator.Common;
using Updator.Common.ChecksumProvider;
using Updator.Common.CompressionProvider;
using Updator.Common.Downloader;
using Updator.Common.Utils;
using DistDescriptionSerializer = Updator.Common.Downloader.DistDescriptionSerializer;
using SourcesSerializer = Updator.Common.Downloader.SourcesSerializer;

namespace Updator.Downloader.UI;

public partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e) {
        base.OnClosing(e);

        Task.Run(async () => {
            await Task.Delay(TimeSpan.FromSeconds(3));
            Environment.Exit(0);
            await Task.Delay(TimeSpan.FromSeconds(2));
            Process.GetCurrentProcess().Kill();
        });
    }

    protected override void OnOpened(EventArgs e) {
        base.OnOpened(e);
        try {
            AppIcon.Loader = new DiskCachedWebImageLoader(Path.Combine(App.AppLocalDataFolder, "cache/image"));
            AppVersion.Content = Strings.LoadingVersion;
            JobName.Content = Strings.Ready;
            AppName.Content = Strings.Update;

            Task.Run(async () => { await InitializeSource(); });
        } catch (Exception ex) {
            App.AppLog.LogError(ex, "错误");
        }
    }

    private string EscapeCommand(string cmd) {
        var escapeList = @"$#&*?;|<>(){}[]~!";
        foreach (char escape in escapeList) {
            cmd = cmd.Replace(escape.ToString(), @$"\{escape}");
        }

        return cmd;
    }

    private void Exec(string cmd) {
        App.AppLog.LogTrace($"系统命令 {cmd}");
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            FileName = "/bin/bash",
            Arguments = $"-c \"{cmd}\""
        };

        process.Start();
        process.WaitForExit();
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        if (process.ExitCode != 0) {
            App.AppLog.LogError("执行失败");
            App.AppLog.LogError("Out: " + stdOut);
            App.AppLog.LogError("Err: " + stdErr);
        } else {
            App.AppLog.LogTrace("执行成功");
            App.AppLog.LogTrace("Out: " + stdOut);
            App.AppLog.LogTrace("Err: " + stdErr);
        }
    }

    private void SetProjectName(string name) {
        if (string.IsNullOrWhiteSpace(name))
            return;

        Dispatcher.UIThread.Invoke(() => { AppName.Content = name; });
    }

    private void SetAppIcon(string icon) {
        if (string.IsNullOrWhiteSpace(icon))
            return;

        Dispatcher.UIThread.Invoke(() => {
            if (AppIcon.Source != icon) {
                AppIcon.Source = icon;
            }
        });
    }

    Source GetSelectedSource(Sources sources) {
        // Select the unique enabled source
        var sourceCandidate = sources.sources.Where(a => a.enable).ToList();
        if (sourceCandidate.Count != 1) {
            if (string.IsNullOrWhiteSpace(sources.defaultSourceId))
                return null;
            return sources.sources.FirstOrDefault(a => a.id == sources.defaultSourceId);
        }

        return sourceCandidate.First();
    }

    private long downloadProgress = 0L;

    void ResetProgressBar() {
        downloadProgress = 0;
        Dispatcher.UIThread.Invoke(() => { JobProgress.Value = 0; });
    }

    void SetProgressBar(double value) {
        Dispatcher.UIThread.Invoke(() => { JobProgress.Value = value; });
    }

    void SetProgressBarMax(double value) {
        Dispatcher.UIThread.Invoke(() => { JobProgress.Maximum = value; });
        ResetProgressBar();
    }

    void IncrementProgressBar(double value) {
        var val = Interlocked.Add(ref downloadProgress, (long)value);
        SetProgressBar(val);
    }

    void SetJobName(string name) {
        Dispatcher.UIThread.Invoke(() => { JobName.Content = name; });
    }

    public async Task InitializeSource() {
        try {
            // Default downloader self-update url.
            var downloaderUrl = "https://dist.reito.fun/downloader";
            var handler = new SocketsHttpHandler() {
                ConnectTimeout = TimeSpan.FromSeconds(60)
            };
            using var http = new HttpClient(handler);
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                $"Updator.Downloader.UI/{Meta.RuntimeString}/{Meta.RuntimeVersion}");
            http.DefaultRequestVersion = HttpVersion.Version11;
            http.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;

            if (OperatingSystem.IsMacOS()) {
                var pwd = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(pwd)) {
                    var match = ".app/Contents/MacOS";
                    if (pwd.Contains(match)) {
                        pwd = pwd[..pwd.IndexOf(match, StringComparison.Ordinal)];

                        var appName = pwd[(pwd.LastIndexOf('/') + 1)..];
                        Environment.SetEnvironmentVariable("UPDATOR_MACOS_APPNAME", appName);

                        pwd = pwd[..pwd.LastIndexOf('/')];
                        Environment.CurrentDirectory = pwd;
                        App.AppLog.LogInformation($"改变工作目录 {Environment.CurrentDirectory} {appName}");
                    }
                }
            }

            // Reads sources.json
            var sourcesPath = Path.Combine(Environment.CurrentDirectory, "sources.json");
            if (!File.Exists(sourcesPath)) {
                var openFileDialog = await Dispatcher.UIThread.Invoke(async () => await StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions() {
                        FileTypeFilter = new[] { new FilePickerFileType("sources.json") { Patterns = new[] { "sources.json" } } },
                        Title = Strings.OpenSourcesFile
                    }
                ));
                var first = openFileDialog.FirstOrDefault();
                if (first != null) {
                    sourcesPath = first.Path.LocalPath;
                    Environment.CurrentDirectory = Path.GetDirectoryName(sourcesPath)!;
                } else {
                    Dispatcher.UIThread.InvokeShutdown();
                }
            }

            App.AppLog.LogInformation($"源：{sourcesPath}");
            App.AppLog.LogInformation($"工作目录 {Environment.CurrentDirectory}");

            Sources sources = null;
            if (File.Exists(sourcesPath)) {
                sources = JsonSerializer.Deserialize(new MemoryStream(await File.ReadAllBytesAsync(sourcesPath)),
                    SourcesSerializer.Default.Sources);
                App.AppLog.LogInformation($"默认名称：{sources.defaultName}");

                SetProjectName(sources.defaultName);
                SetAppIcon(sources.defaultIcon ?? "Icon.ico");
            }

            App.AppLog.LogInformation($"启动更新");

            // If there's custom downloader url, replace it
            if (sources != null && !string.IsNullOrWhiteSpace(sources.customDownloaderUrl)) {
                downloaderUrl = sources.customDownloaderUrl;
                App.AppLog.LogInformation($"自定义下载源 {downloaderUrl}");
            }

            var latestDownloaderVersion = 0;
            try {
                if (int.TryParse(await http.GetStringAsync(Path.Combine(downloaderUrl, $"{Meta.RuntimeString}-build-id")), out var v)) {
                    latestDownloaderVersion = v;
                }
            } catch (Exception ex) {
                Popup.Exception(Strings.RequestFailed, ex);
            }

            App.AppLog.LogInformation($"启动器最新版本 {latestDownloaderVersion} {Meta.RuntimeVersion}");

            if (latestDownloaderVersion > Meta.RuntimeVersion) {
                App.AppLog.LogInformation($"请求更新");

                var result = await Dispatcher.UIThread.Invoke(async () => {
                    return await MessageBoxManager.GetMessageBoxCustom(new MessageBoxCustomParams() {
                            ContentTitle = Strings.Update,
                            ContentMessage = string.Format(Strings.UpdateDownloaderAsk, Meta.RuntimeVersion, latestDownloaderVersion),
                            ButtonDefinitions = new ButtonDefinition[] {
                                new() {
                                    Name = Strings.Yes
                                },
                                new() {
                                    Name = Strings.No
                                }
                            },
                            FontFamily = App.FontFamily,
                            WindowStartupLocation = WindowStartupLocation.CenterScreen
                        })
                        .ShowAsync();
                });

                if (result == Strings.Yes) {
                    App.AppLog.LogInformation($"执行更新");
                    SetJobName(Strings.UpdateDownloader);

                    var signature = await http.GetByteArrayAsync(Path.Combine(downloaderUrl, $"ui-{Meta.RuntimeString}.sha512"));

                    // Download with progress
                    // TODO: Extract this code in a extension.
                    var resp = await http.GetAsync(Path.Combine(downloaderUrl, $"ui-{Meta.RuntimeString}"),
                        HttpCompletionOption.ResponseHeadersRead);
                    var len = resp.Content.Headers.ContentLength!.Value;

                    SetProgressBarMax(len);

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
                        App.AppLog.LogInformation($"更新校验完成");

                        if (OperatingSystem.IsMacOS()) {
                            var name = Environment.GetEnvironmentVariable("UPDATOR_MACOS_APPNAME");
                            var old = Path.Combine(Environment.CurrentDirectory, $"{name}.app");
                            var deleteParam = (name == null || !Directory.Exists(old)) ? string.Empty : $"--delete '{old}'";

                            name ??= "启动器";
                            var file = $"{name}.{latestDownloaderVersion}.zip";
                            var app = $"{name}.{latestDownloaderVersion}.app";
                            await File.WriteAllBytesAsync(file, payload);

                            App.AppLog.LogInformation($"MacOS 更新 {app} {deleteParam}");
                            Exec($"rm -rf build.app");
                            Exec($"ditto -x -k '{file}' .");
                            Exec($"mv 'build.app' '{app}'");
                            Exec($"open '{app}' --args {deleteParam}");
                            Exec($"rm -f '{file}'");
                        } else if (OperatingSystem.IsLinux()) {
                            var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath)!;
                            var file = Path.GetTempFileName();
                            await File.WriteAllBytesAsync(file, payload);
                            Exec($"chmod +x '{file}'");
                            Process.Start(new ProcessStartInfo() {
                                FileName = file,
                                CreateNoWindow = false,
                                Arguments = $"--updateSelf --programPath \"{Environment.ProcessPath}\" --programName \"{name}\"",
                                UseShellExecute = true
                            });
                        } else {
                            var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath)!;
                            var file = Path.ChangeExtension(Path.GetTempFileName(), "exe");
                            await File.WriteAllBytesAsync(file, payload);
                            Process.Start(new ProcessStartInfo() {
                                FileName = file,
                                CreateNoWindow = false,
                                Arguments = $"--updateSelf --programPath \"{Environment.ProcessPath}\" --programName \"{name}\"",
                                UseShellExecute = true
                            });
                        }

                        Dispatcher.UIThread.InvokeShutdown();
                    } else {
                        Popup.Exception("更新校验失败");
                    }

                    return;
                }
            }

            if (sources == null) {
                Popup.Exception(Strings.SourcesNotFound);
                return;
            }

            // Update sources.json if set.
            App.AppLog.LogInformation($"检查软件源更新");
            if (!string.IsNullOrWhiteSpace(sources.sourcesUrl) && !sources.disableSourcesUpdate) {
                try {
                    SetJobName(Strings.UpdateSourcesJson);

                    App.AppLog.LogInformation($"请求新软件源");
                    var newSources = await http.GetByteArrayAsync(sources.sourcesUrl);
                    SetProgressBar(80);

                    var newSourcesObj = await JsonSerializer.DeserializeAsync(new MemoryStream(newSources),
                        SourcesSerializer.Default.Sources);
                    SetProgressBar(95);

                    App.AppLog.LogInformation($"解析新软件源 {newSourcesObj.version} {sources.version}");

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

            App.AppLog.LogInformation($"更新软件源完成");

            var source = GetSelectedSource(sources);
            if (source == null) {
                Popup.Exception(Strings.ShouldUniqueSource);
                return;
            }

            SetProjectName(source.defaultName);
            SetAppIcon(sources.defaultIcon);
            App.AppLog.LogInformation($"选择软件源 {source.id} {source.defaultName}");

            DistDescription desc;
            SetJobName(Strings.DownloadDescription);
            App.AppLog.LogInformation($"下载软件描述");

            // Download description file for the distribution.
            try {
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
            SetAppIcon(desc.appIconUrl);
            Dispatcher.UIThread.Invoke(() => { AppVersion.Content = $"{desc.versionString} ({desc.buildId})"; });

            App.AppLog.LogInformation($"下载软件描述完成");

            // Restore compression provider
            ICompressionProvider compress = desc.compression switch {
                "brotli" => new Brotli(),
                "gzip" => new GZip(),
                _ => new Raw()
            };

            // Restore checksum provider
            IChecksumProvider check = desc.checksum switch {
                "crc64" => new ChecksumCosCrc64(),
                "azure-md5" => new ChecksumAzureMd5(),
                _ => null
            };

            if (check == null) {
                Popup.Exception(Strings.RequestFailed);
                return;
            }

            SetJobName(Strings.DownloadUpdateFiles);
            var distRoot = new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, desc.channel)).FullName;
            var descPath = Path.Combine(distRoot, "__description.json");

            App.AppLog.LogInformation($"写入目录 {distRoot}");

            if (!Directory.Exists(distRoot)) {
                Directory.CreateDirectory(distRoot);
            }

            var totalDownloadSize = desc.files.Sum(a => a.downloadSize);
            var legacyDownloadProgress = totalDownloadSize == 0;
            SetProgressBarMax(legacyDownloadProgress ? desc.files.Count : totalDownloadSize);

            var displayUpdateLog = false;
            App.AppLog.LogInformation($"合并历史描述");

            // If there's an old description file, and have update logs
            if (File.Exists(descPath)) {
                try {
                    var oldDesc = await JsonSerializer.DeserializeAsync(new MemoryStream(await File.ReadAllBytesAsync(descPath)),
                        DistDescriptionSerializer.Default.DistDescription);

                    // If any of the reinstall build id is larger than current 
                    if (desc.reinstallBuildId is { Count: > 0 }) {
                        foreach (var id in desc.reinstallBuildId) {
                            if (oldDesc.buildId < id) {
                                App.AppLog.LogInformation($"清空历史文件");
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

            App.AppLog.LogInformation($"执行更新");

            // Compare checksum and download if mismatch.
            // Use parallel to speed up.
            var cts = new CancellationTokenSource();
            await Parallel.ForEachAsync(desc.files, new ParallelOptions() {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cts.Token
            }, async (f, ct) => {
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
                    var retryCount = 0;
                    while (true) {
                        try {
                            await http.DownloadAsync(Path.Combine(source.distributionUrl, f.objectKey), fullPath, check, f.checksum, compress, ct,
                                tuple => {
                                    if (!legacyDownloadProgress) {
                                        IncrementProgressBar(tuple.BlockRead);
                                    }
                                });
                            break;
                        } catch (TaskCanceledException) {
                            // ignored
                        } catch (Exception ex) {
                            App.AppLog.LogError(ex, $"下载错误");
                            if (retryCount++ < 3) {
                                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                            } else {
                                if (!cts.IsCancellationRequested) {
                                    await cts.CancelAsync();
                                    Popup.Exception(Strings.DownloadFileFailed, ex);
                                }

                                break;
                            }
                        }
                    }
                } else {
                    IncrementProgressBar(f.downloadSize);
                }

                if (legacyDownloadProgress) {
                    IncrementProgressBar(1);
                }
            });

            if (cts.IsCancellationRequested) {
                return;
            }

            await File.WriteAllTextAsync(descPath, JsonSerializer.Serialize(desc, DistDescriptionSerializer.Default.DistDescription), cts.Token);

            var passArgument = string.Empty;
            if (!string.IsNullOrWhiteSpace(desc.passBuildId)) {
                passArgument += $"--{desc.passBuildId} {desc.buildId} ";
            }

            SetJobName(string.Format(Strings.UpdateDone, desc.projectName));

            if (Design.IsDesignMode) {
                return;
            }

            App.AppLog.LogInformation($"启动软件");

            // Start the payload executable  
            var executable = Path.Combine(distRoot, desc.executable);
            if (!OperatingSystem.IsWindows()) {
                Exec($"chmod +x '{executable}'");
                Process.Start(new ProcessStartInfo {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    FileName = "/bin/bash",
                    WorkingDirectory = new DirectoryInfo(executable).Parent!.FullName,
                    Arguments = $"-c \"nohup '{executable}' >/dev/null 2>&1 &\""
                });
            } else {
                Process.Start(new ProcessStartInfo() {
                    FileName = executable,
                    WorkingDirectory = new DirectoryInfo(executable).Parent!.FullName,
                    Arguments = passArgument,
                    UseShellExecute = true
                });
            }

            if (displayUpdateLog && !string.IsNullOrWhiteSpace(desc.updateLogUrl)) {
                var result = await Dispatcher.UIThread.Invoke(async () => {
                    return await MessageBoxManager.GetMessageBoxCustom(new MessageBoxCustomParams() {
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
                            FontFamily = App.FontFamily,
                            WindowStartupLocation = WindowStartupLocation.CenterScreen
                        })
                        .ShowAsync();
                });

                if (result == Strings.Yes) {
                    Process.Start(new ProcessStartInfo(desc.updateLogUrl) {
                        UseShellExecute = true
                    });
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);
            Dispatcher.UIThread.InvokeShutdown();
        } catch (Exception ex) {
            App.AppLog.LogError(ex, "错误");
        }
    }
}