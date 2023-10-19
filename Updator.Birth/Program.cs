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
using Updator.Common.CompressionProvider;
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

async Task<byte[]> DecompressBrotli(byte[] data) {
   using var decompressed = new MemoryStream();
   using var compressed = new MemoryStream(data);
   var brotli = new Brotli();
   await brotli.Decompress(compressed, decompressed);
   decompressed.Position = 0;
   return decompressed.ToArray();
}

ConcurrentBag<string> keys = new();
await Parallel.ForEachAsync(config.projects, new ParallelOptions() { MaxDegreeOfParallelism = 4 }, async (project, _) => {
   Console.WriteLine($@"Publish {project.name}");
   var path = Path.Combine(birthDir, project.name, "sources.json");
   if (!File.Exists(path)) {
      Console.WriteLine("File not found.");
      return;
   }

   using var http = new HttpClient();
   var bin = await http.GetByteArrayAsync($"https://direct.dist.reito.fun/downloader/ui-{project.platform}");
   bin = await DecompressBrotli(bin);

   using var ms = new MemoryStream();
   var zip = ZipFile.Create(ms);

   zip.BeginUpdate();
   zip.SetComment("请解压至任意文件夹使用，不要直接在压缩包中打开！");

   var name = string.IsNullOrWhiteSpace(project.display) ? project.name : project.display;
   if (project.platform == "osx") {
      var temp = Path.GetTempFileName();
      var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()).Replace("\\", "/");
      Directory.CreateDirectory(dir);
      File.WriteAllBytes(temp, bin);
      System.IO.Compression.ZipFile.ExtractToDirectory(temp, dir, Encoding.UTF8, true);

      var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
      foreach (string file in files) {
         var relativePath = file.Replace("\\", "/").Replace(dir, string.Empty);
         var bytes = File.ReadAllBytes(file);
         var e = new ZipEntry(
            $"{name}/{name}{config.suffix}.app{relativePath}") {
            IsUnicodeText = true,
            HostSystem = 3,
            ExternalFileAttributes = relativePath.EndsWith("Contents/MacOS/Updator.Downloader.UI") ? 0x81ed << 16 : 0x81a4 << 16,
            Size = bytes.Length,
            Crc = BitConverter.ToInt32(Crc32.Hash(bytes))
         };
         zip.Add(new MemoryDataSource(bytes), e);
      }
   } else {
      var exe = new ZipEntry(
         $"{name}/{name}{config.suffix}{(project.platform == "win" ? ".exe" : string.Empty)}") {
         IsUnicodeText = true,
         HostSystem = 3,
         ExternalFileAttributes = 0x81ed << 16,
         Size = bin.Length,
         Crc = BitConverter.ToInt32(Crc32.Hash(bin))
      };
      zip.Add(new MemoryDataSource(bin), exe);
   }

   var sources = File.ReadAllBytes(path);
   var src = new ZipEntry($"{name}/sources.json") {
      IsUnicodeText = true,
      HostSystem = 3,
      ExternalFileAttributes = 0x81a4 << 16,
      Size = sources.Length,
      Crc = BitConverter.ToInt32(Crc32.Hash(sources))
   };

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
