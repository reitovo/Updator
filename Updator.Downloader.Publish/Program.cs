// See https://aka.ms/new-console-template for more information

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommandLine;
using Octokit;
using Octokit.Internal;
using Updator.Common.CompressionProvider;
using Updator.Downloader.CLI;
using Updator.Downloader.Publish;
using Uploader.StorageProvider;
using Meta = Updator.Common.Downloader.Meta;

Console.OutputEncoding = Encoding.UTF8;

var parsed = new Parser(a => {
   a.AllowMultiInstance = true;
   a.IgnoreUnknownArguments = true;
}).ParseArguments<Options>(args);
if (parsed.Value == null) {
   return -1;
}

var opt = parsed.Value;

// Read tokens from file provided via args[1]
var config = await JsonSerializer.DeserializeAsync<Config>(new MemoryStream(Convert.FromBase64String(opt.Config)));

// I use Tencent COS as a distributor
var storage = new TencentCos(config.cos);

// Local function for publish a runtime
async Task PublishWin() {
   var runtime = "win";
   Console.WriteLine($@"Publish {runtime}");
   var path = opt.Path;
   await UploadPackage(runtime, path);

   if (opt.Legacy) {
      var data = File.ReadAllBytes(path);
      var hash = SHA512.HashData(data);
      await Upload($"cli-{runtime}-x64.sha512", hash);
      await Upload($"cli-{runtime}-x64", data);
      await Upload($"brotli-cli-{runtime}-x64", await CompressBrotli(data));
   }
}

async Task PublishMac() {
   var runtime = "osx";
   Console.WriteLine($@"Publish {runtime}");
   var path = opt.Path;
   var pack = opt.Path + ".zip";
   ZipFile.CreateFromDirectory(path, pack, CompressionLevel.SmallestSize, false, Encoding.UTF8);
   await UploadPackage(runtime, pack);

   if (opt.Legacy) {
      var data = File.ReadAllBytes($"{path}/Contents/MacOS/Updator.Downloader.UI");
      var hash = SHA512.HashData(data);
      await Upload($"cli-{runtime}-x64.sha512", hash);
      await Upload($"cli-{runtime}-x64", data);
      await Upload($"brotli-cli-{runtime}-x64", await CompressBrotli(data));
   }
}

async Task PublishLinux() {
   var runtime = "linux";
   Console.WriteLine($@"Publish {runtime}");
   var path = opt.Path;
   await UploadPackage(runtime, path);

   if (opt.Legacy) {
      var data = File.ReadAllBytes(path);
      var hash = SHA512.HashData(data);
      await Upload($"cli-{runtime}-x64.sha512", hash);
      await Upload($"cli-{runtime}-x64", data);
      await Upload($"brotli-cli-{runtime}-x64", await CompressBrotli(data));
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

async Task UploadPackage(string os, string outputFile) {
   var data = File.ReadAllBytes(outputFile);
   var hash = SHA512.HashData(data);
   await Upload($"ui-{os}.sha512", hash);
   await Upload($"ui-{os}", await CompressBrotli(data));
   await Upload($"{os}-build-id", Encoding.UTF8.GetBytes(Meta.VersionByRuntime[os].ToString()));
}

switch (opt.Os) {
   case "win":
      await PublishWin();
      break;
   case "osx":
      await PublishMac();
      break;
   case "linux":
      await PublishLinux();
      break;
}

// Write version file (legacy)
if (opt.Legacy) {
   await Upload("build-id", Encoding.UTF8.GetBytes(Meta.WinVersion.ToString()));
}

async Task Upload(string name, byte[] data) {
   Console.WriteLine($@"Upload Tencent Cos {name}");
   await using var ms = new MemoryStream(data);
   await storage.UploadAsync(name, ms);
}

Console.WriteLine(@"Refresh CDN");
await storage.RefreshRoot();

Console.WriteLine(@"Done Tencent Cos");
return 0;

file class Options {
   [Option("os", Required = false)]
   public string Os { get; set; }
   [Option("path", Required = false)]
   public string Path { get; set; }
   [Option("config", Required = false)]
   public string Config { get; set; }
   [Option("legacy", Required = false)]
   public bool Legacy { get; set; }
}
