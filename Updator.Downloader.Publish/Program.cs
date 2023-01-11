// See https://aka.ms/new-console-template for more information

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

Console.OutputEncoding = Encoding.UTF8;

var buildId = DownloaderMeta.Version;
var projectDir = args[0];
var publishDir = Path.Combine(projectDir, "bin/Publish");
Directory.Delete(Path.Combine(projectDir, "bin/Release/net7.0"), true);
Directory.Delete(publishDir, true);
Directory.CreateDirectory(publishDir);

var config = await JsonSerializer.DeserializeAsync<Config>(new MemoryStream(File.ReadAllBytes(args[1])));
var storage = new TencentCos(config.cos);

async Task Publish(string runtime) {
   var proc = Process.Start(new ProcessStartInfo() {
      FileName = "dotnet",
      Arguments =
         $"publish -r {runtime} -c Release --self-contained --framework net7.0 -p:PublishSingleFile=true -p:PublishTrimmed=true",
      WorkingDirectory = projectDir,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      StandardOutputEncoding = Encoding.UTF8,
      StandardErrorEncoding = Encoding.UTF8
   })!;
   await proc.WaitForExitAsync();
   Console.WriteLine(await proc.StandardOutput.ReadToEndAsync());
   Console.WriteLine(await proc.StandardError.ReadToEndAsync());
}

var osList = new string[] {"win-x64", "osx-x64", "linux-x64"};
await Parallel.ForEachAsync(osList, new ParallelOptions() {MaxDegreeOfParallelism = 1}, async (os, _) => {
   Console.WriteLine($@"Publish {os}");
   await Publish(os);
   var outputFile = Path.Combine(projectDir, $"bin/Release/net7.0/{os}/publish/Updator.Downloader.CLI");
   if (os == "win-x64") {
      outputFile += ".exe";
   }
   File.Copy(outputFile, Path.Combine(publishDir, $"cli-{os}"), true);
   var hash = SHA512.HashData(File.ReadAllBytes(outputFile));
   File.WriteAllBytes(Path.Combine(publishDir, $"cli-{os}.sha512"), hash);
});
File.WriteAllText(Path.Combine(publishDir, $"build-id"), buildId.ToString());

Console.WriteLine(@"Done Generate");

Console.WriteLine(@"Upload Tencent Cos");

ConcurrentBag<string> keys = new();
await Parallel.ForEachAsync(new DirectoryInfo(publishDir).GetFiles(), async (f, _) => {
   Console.WriteLine($@"Upload Tencent Cos {f.Name}");
   await using var fs = f.OpenRead();
   await storage.UploadAsync(f.Name, fs);
   keys.Add(f.Name);
});

Console.WriteLine(@"Refresh CDN");
await storage.RefreshObjectKeys(keys);

Console.WriteLine(@"Done Tencent Cos");

// this is the core connection
var connection = new Connection(new ProductHeaderValue("Updator-Publish"),
   new HttpClientAdapter(() => HttpMessageHandlerFactory.CreateDefault(new WebProxy() /*HttpClient.DefaultProxy*/)));
var client = new GitHubClient(connection) {
   Credentials = new Credentials(config.githubToken)
};

var tag = $"build-id-{buildId}";
var repo = await client.Repository.Get("cnSchwarzer", "Updator");
try {
   Console.WriteLine($@"Delete Github Release {tag}");
   var rel = await client.Repository.Release.Get(repo.Id, tag);
   await client.Repository.Release.Delete(repo.Id, rel.Id);
} catch (Exception) {
   // ignored
}

Console.WriteLine($@"Create Github Release {tag}");
var body = $"""
                # Updator Downloader Release
                Build ID: {buildId}
                Release Date: {DateTime.Now}
                """;
var release = await client.Repository.Release.Create(repo.Id, new NewRelease(tag) {
   Body = body,
   Name = tag
});

await Parallel.ForEachAsync(new DirectoryInfo(publishDir).GetFiles(), async (f, _) => {
   Console.WriteLine($@"Upload Github {f.Name}");
   await using var fs = f.OpenRead();
   await client.Repository.Release.UploadAsset(release,
      new ReleaseAssetUpload(f.Name, "application/octet-stream", fs, null));
});

Console.WriteLine(@"Done Upload Github");