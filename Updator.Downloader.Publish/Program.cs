﻿// See https://aka.ms/new-console-template for more information

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Octokit;
using Octokit.Internal;
using Updator.Downloader.CLI;
using Updator.Downloader.Publish;
using Uploader.StorageProvider;
using Meta = Updator.Common.Downloader.Meta;

Console.OutputEncoding = Encoding.UTF8;

// Get current downloader version
var buildId = Meta.Version;

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

// Local function for publish a runtime
async Task Publish(string runtime) {
   var proc = Process.Start(new ProcessStartInfo() {
      FileName = "dotnet",
      Arguments =
         $"publish -r {runtime} -c Release --self-contained --framework net7.0 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=true -p:IncludeAllContentForSelfExtract=true",
      WorkingDirectory = projectDir,
      UseShellExecute = false
   })!;
   await proc.WaitForExitAsync();
}

// Publish platforms
var osList = new string[] { "win-x64", "osx-x64", "linux-x64" };
// CANNOT use parallel as it will invalidate builds. Set max degree of parallelism to 1
await Parallel.ForEachAsync(osList, new ParallelOptions() { MaxDegreeOfParallelism = 1 }, async (os, _) => {
   Console.WriteLine($@"Publish {os}");
   await Publish(os);
   var outputFile = Path.Combine(projectDir, $"bin/Release/net7.0/{os}/publish/Updator.Downloader.UI");
   if (os == "win-x64") {
      outputFile += ".exe";
   }
   File.Copy(outputFile, Path.Combine(publishDir, $"cli-{os}"), true);
   var hash = SHA512.HashData(File.ReadAllBytes(outputFile));
   File.WriteAllBytes(Path.Combine(publishDir, $"cli-{os}.sha512"), hash);
});
// Write version file
File.WriteAllText(Path.Combine(publishDir, $"build-id"), buildId.ToString());
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
            Version: {buildId}
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
