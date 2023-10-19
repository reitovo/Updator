// See https://aka.ms/new-console-template for more information

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ICSharpCode.SharpZipLib.Checksum;
using ICSharpCode.SharpZipLib.Zip;
using Updator.Birth;
using Updator.Common.Downloader;
using Updator.Downloader.CLI;
using Uploader.StorageProvider;
using Crc32 = System.IO.Hashing.Crc32;
using ZipFile = ICSharpCode.SharpZipLib.Zip.ZipFile;

Console.OutputEncoding = Encoding.UTF8; 

// Get projects root from args[0]
var birthDir = args[0];
var config =
   await JsonSerializer.DeserializeAsync<BirthConfig>(
      new MemoryStream(File.ReadAllBytes(Path.Combine(birthDir, "config.json"))));

// I use Tencent COS as a distributor
var storage = new TencentCos(config.cos);

ConcurrentBag<string> keys = new();
await Parallel.ForEachAsync(config.projects, new ParallelOptions() { MaxDegreeOfParallelism = 4 }, async (project, _) => {
   Console.WriteLine($@"Publish {project.name}");
   var path = Path.Combine(birthDir, project.name, "sources.json");
   if (!File.Exists(path)) {
      Console.WriteLine("File not found.");
      return;
   }

   using var http = new HttpClient();
   var bin = await http.GetByteArrayAsync($"https://direct.dist.reito.fun/downloader/cli-{project.platform}-x64");

   using var ms = new MemoryStream();
   var zip = ZipFile.Create(ms);

   var name = string.IsNullOrWhiteSpace(project.display) ? project.name : project.display;
   var exe = new ZipEntry(
      $"{name}/{name}{config.suffix}{(project.platform == "win" ? ".exe" : string.Empty)}") {
      IsUnicodeText = true,
      HostSystem = 3,
      ExternalFileAttributes = 0x81ed << 16,
      Size = bin.Length,
      Crc = BitConverter.ToInt32(Crc32.Hash(bin))
   };

   var sources = File.ReadAllBytes(path);
   var src = new ZipEntry($"{name}/sources.json") {
      IsUnicodeText = true,
      HostSystem = 3,
      ExternalFileAttributes = 0x81a4 << 16,
      Size = sources.Length,
      Crc = BitConverter.ToInt32(Crc32.Hash(sources))
   };

   zip.BeginUpdate();
   zip.SetComment("请解压至任意文件夹使用，不要直接在压缩包中打开！");
   zip.Add(new MemoryDataSource(bin), exe);
   zip.Add(new MemoryDataSource(sources), src);
   zip.CommitUpdate();

   zip.Close();

   var z = ms.ToArray();
 
   File.WriteAllBytes(Path.Combine(birthDir, project.name, $"{project.name}.zip"), z);

   Console.WriteLine($@"Upload Tencent Cos {project.name}");
   await storage.UploadAsync($"{project.name}.zip", ms);
   keys.Add($"{project.name}.zip");

});

Console.WriteLine(@"Refresh CDN");
await storage.RefreshObjectKeys(keys);

Console.WriteLine(@"Done Tencent Cos");

class MemoryDataSource : IStaticDataSource {
   private readonly MemoryStream _ms;

   public MemoryDataSource(byte[] b) {
      _ms = new MemoryStream(b);
   }

   public Stream GetSource() {
      return _ms;
   }
}
