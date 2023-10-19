// See https://aka.ms/new-console-template for more information

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Octokit;
using Octokit.Internal;
using Updator.Common.CompressionProvider;
using Updator.Downloader.CLI;
using Updator.Downloader.Publish;
using Uploader.StorageProvider;
using Meta = Updator.Common.Downloader.Meta;

Console.OutputEncoding = Encoding.UTF8;

Environment.SetEnvironmentVariable("UPDATOR_BUILD_LEGACY", "0");
Environment.SetEnvironmentVariable("UPDATOR_BUILD_WIN", "0");
Environment.SetEnvironmentVariable("UPDATOR_BUILD_MAC", "1");
Environment.SetEnvironmentVariable("UPDATOR_BUILD_LINUX", "0");
Environment.SetEnvironmentVariable("UPDATOR_UPLOAD_GITHUB", "0");

// Get project root from args[0]
var projectDir = args[0];
var publishDir = Path.Combine(projectDir, "bin/Publish");

// Remove old builds
if (Directory.Exists(Path.Combine(projectDir, "bin/Release/net7.0")))
   Directory.Delete(Path.Combine(projectDir, "bin/Release/net7.0"), true);
if (Directory.Exists(publishDir))
   Directory.Delete(publishDir, true);
Directory.CreateDirectory(publishDir);

// Read tokens from file provided via args[1]
var config = await JsonSerializer.DeserializeAsync<Config>(new MemoryStream(File.ReadAllBytes(args[1])));

// I use Tencent COS as a distributor
var storage = new TencentCos(config.cos);

var buildLegacy = Environment.GetEnvironmentVariable("UPDATOR_BUILD_LEGACY") == "1";
var buildWin = Environment.GetEnvironmentVariable("UPDATOR_BUILD_WIN") == "1";
var buildMac = Environment.GetEnvironmentVariable("UPDATOR_BUILD_MAC") == "1";
var buildLinux = Environment.GetEnvironmentVariable("UPDATOR_BUILD_LINUX") == "1";
var uploadGithub = Environment.GetEnvironmentVariable("UPDATOR_UPLOAD_GITHUB") == "1";

// Local function for publish a runtime
async Task PublishWin() {
   if (!buildWin) {
      return;
   }
   var runtime = "win";
   Console.WriteLine($@"Publish {runtime}");
   var proc = Process.Start(new ProcessStartInfo() {
      FileName = "dotnet",
      Arguments =
         $"publish -r {runtime}-x64 -c Release --self-contained --framework net7.0",
      WorkingDirectory = projectDir,
      UseShellExecute = false
   })!;
   await proc.WaitForExitAsync();
   var path = Path.Combine(projectDir, $"bin/Release/net7.0/{runtime}-x64/publish/Updator.Downloader.UI.exe");
   await WritePackage(runtime, path);

   if (buildLegacy) {
      var data = File.ReadAllBytes(Path.Combine(projectDir, $"bin/Release/net7.0/{runtime}-x64/publish/Updator.Downloader.UI.exe"));
      var hash = SHA512.HashData(data);
      File.WriteAllBytes(Path.Combine(publishDir, $"cli-{runtime}-x64.sha512"), hash);
      File.WriteAllBytes(Path.Combine(publishDir, $"cli-{runtime}-x64"), data);
      File.WriteAllBytes(Path.Combine(publishDir, $"brotli-cli-{runtime}-x64"), await CompressBrotli(data));
   }
}

async Task PublishMac() {
   if (!buildMac) {
      return;
   }
   var runtime = "osx";
   Console.WriteLine($@"Publish {runtime}");
   var proc = Process.Start(new ProcessStartInfo() {
      FileName = "dotnet",
      Arguments =
         $"publish -r {runtime}-x64 -c Release --self-contained --framework net7.0",
      WorkingDirectory = projectDir,
      UseShellExecute = false
   })!;
   await proc.WaitForExitAsync();
   proc = Process.Start(new ProcessStartInfo() {
      FileName = "dotnet",
      Arguments =
         $"msbuild -t:BundleApp -p:CFBundleShortVersionString={Meta.MacVersion} -p:Configuration=Release -p:RuntimeIdentifier=osx-x64 -p:SelfContained=true",
      WorkingDirectory = projectDir,
      UseShellExecute = false
   })!;
   await proc.WaitForExitAsync();
   var path = Path.Combine(projectDir, $"bin/Release/net7.0/{runtime}-x64/publish/启动器.app");
   var pack = Path.Combine(projectDir, $"bin/Release/net7.0/{runtime}-x64/publish/启动器.app.zip");
   ZipFile.CreateFromDirectory(path, pack, CompressionLevel.SmallestSize, false, Encoding.UTF8);
   await WritePackage(runtime, pack);

   if (buildLegacy) {
      var data = File.ReadAllBytes(Path.Combine(projectDir, $"bin/Release/net7.0/{runtime}-x64/publish/Updator.Downloader.UI"));
      var hash = SHA512.HashData(data);
      File.WriteAllBytes(Path.Combine(publishDir, $"cli-{runtime}-x64.sha512"), hash);
      File.WriteAllBytes(Path.Combine(publishDir, $"cli-{runtime}-x64"), data);
      File.WriteAllBytes(Path.Combine(publishDir, $"brotli-cli-{runtime}-x64"), await CompressBrotli(data));
   }
}

async Task PublishLinux() {
   if (!buildLinux) {
      return;
   }

   var runtime = "linux";
   Console.WriteLine($@"Publish {runtime}");
   var proc = Process.Start(new ProcessStartInfo() {
      FileName = "dotnet",
      Arguments =
         $"publish -r {runtime}-x64 -c Release --self-contained --framework net7.0",
      WorkingDirectory = projectDir,
      UseShellExecute = false
   })!;
   await proc.WaitForExitAsync();
   var path = Path.Combine(projectDir, $"bin/Release/net7.0/{runtime}-x64/publish/Updator.Downloader.UI");
   await WritePackage(runtime, path);

   if (buildLegacy) {
      var data = File.ReadAllBytes(Path.Combine(projectDir, $"bin/Release/net7.0/{runtime}-x64/publish/Updator.Downloader.UI"));
      var hash = SHA512.HashData(data);
      File.WriteAllBytes(Path.Combine(publishDir, $"cli-{runtime}-x64.sha512"), hash);
      File.WriteAllBytes(Path.Combine(publishDir, $"cli-{runtime}-x64"), data);
      File.WriteAllBytes(Path.Combine(publishDir, $"brotli-cli-{runtime}-x64"), await CompressBrotli(data));
   }
}

async Task<byte[]> CompressBrotli(byte[] data) {
   using var decompressed = new MemoryStream(data);
   using var compressed = new MemoryStream();
   var brotli = new Brotli();
   await brotli.Compress(decompressed, compressed);
   compressed.Position = 0;
   return compressed.ToArray();
}

async Task WritePackage(string os, string outputFile) {
   var hash = SHA512.HashData(File.ReadAllBytes(outputFile));
   File.WriteAllBytes(Path.Combine(publishDir, $"ui-{os}.sha512"), hash);

   using var decompressed = new MemoryStream(File.ReadAllBytes(outputFile));
   using var compressed = new MemoryStream();
   var brotli = new Brotli();
   await brotli.Compress(decompressed, compressed);
   compressed.Position = 0;
   File.WriteAllBytes(Path.Combine(publishDir, $"ui-{os}"), compressed.ToArray());
   File.WriteAllText(Path.Combine(publishDir, $"{os}-build-id"), Meta.VersionByRuntime[os].ToString());
}

await PublishWin();
await PublishMac();
await PublishLinux();

// Write version file (legacy)
if (buildLegacy) {
   File.WriteAllText(Path.Combine(publishDir, $"build-id"), "57");
}
Console.WriteLine(@"Done Generate");

// Upload to tencent cos
Console.WriteLine(@"Upload Tencent Cos");
ConcurrentBag<string> keys = new();
await Parallel.ForEachAsync(new DirectoryInfo(publishDir).GetFiles(), async (f, _) => {
   Console.WriteLine($@"Upload Tencent Cos {f.Name}");
   await using var fs = f.OpenRead();
   await storage.UploadAsync(f.Name, fs);
   keys.Add(f.Name);
});

Console.WriteLine(@"Refresh CDN");
await storage.RefreshRoot();

Console.WriteLine(@"Done Tencent Cos");

// Upload to github
if (uploadGithub) {
   var connection = new Connection(new ProductHeaderValue("Updator-Publish"),
      new HttpClientAdapter(() => HttpMessageHandlerFactory.CreateDefault(new WebProxy() /*HttpClient.DefaultProxy*/)));
   var client = new GitHubClient(connection) {
      Credentials = new Credentials(config.githubToken)
   };

   var tag = $"build-{DateTime.Now:yyyy-MM-dd}";
   var repo = await client.Repository.Get("cnSchwarzer", "Updator");
   try {
      Console.WriteLine($@"Delete Github Release {tag}");
      var rel = await client.Repository.Release.Get(repo.Id, tag);
      await client.Repository.Release.Delete(repo.Id, rel.Id);
   } catch (Exception) {
      // ignored
   }

   // Create github release
   Console.WriteLine($@"Create Github Release {tag}");
   var body = $"""
               # Updator Downloader Release
               Date: {DateTime.Now}
               This release is generated by tool!
               You should download this downloader from software which uses this updater framework, as it won't work without `sources.json` provided by each software distributor.
               """;
   var release = await client.Repository.Release.Create(repo.Id, new NewRelease(tag) {
      Body = body,
      Name = tag
   });

   // Upload assets
   await Parallel.ForEachAsync(new DirectoryInfo(publishDir).GetFiles(), async (f, _) => {
      Console.WriteLine($@"Upload Github {f.Name}");
      await using var fs = f.OpenRead();
      await client.Repository.Release.UploadAsset(release,
         new ReleaseAssetUpload(f.Name, "application/octet-stream", fs, null));
   });

   Console.WriteLine(@"Done Upload Github");
}
