// See https://aka.ms/new-console-template for more information

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommandLine;
using Updator.Common.CompressionProvider;
using Updator.Common.StorageProvider;
using Updator.Downloader.Publish;
using Meta = Updator.Common.Downloader.Meta;

Console.OutputEncoding = Encoding.UTF8;
var objectKeys = new List<string>();

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

// Initialize storage provider based on config
IStorageProvider storage = (config.storageType ?? "cos") switch {
   "cos" => new TencentCos(config.cos),
   "azure" or "azure-blobs" => new AzureBlobs(config.azure),
   "s3" => new S3Compatible(config.s3),
   _ => throw new Exception($"Unknown storage type: {config.storageType}")
};
ICdnRefresh cdnRefresh = storage as ICdnRefresh;

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
   await UploadPackage(runtime, path);

   if (opt.Legacy) {
      var data = File.ReadAllBytes($"{Path.ChangeExtension(path, "app")}/Contents/MacOS/Updator.Downloader.UI");
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
   Console.WriteLine($@"Upload to {config.storageType ?? "cos"}: {name}");
   await using var ms = new MemoryStream(data);
   await storage.UploadAsync(name, ms);
   objectKeys.Add(name);
}

Console.WriteLine(@"Refresh CDN");
if (cdnRefresh != null) {
   await cdnRefresh.CdnPurgePath();
   await cdnRefresh.CdnPrefetchObjectKeys(objectKeys);
} else {
   Console.WriteLine(@"CDN refresh not supported for this storage provider");
}

Console.WriteLine(@"Done");
return 0;

namespace Updator.Downloader.Publish {
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
}
