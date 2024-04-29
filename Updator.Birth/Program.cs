// See https://aka.ms/new-console-template for more information

using System.Text;
using System.Text.Json;
using CommandLine;
using ICSharpCode.SharpZipLib.Zip;
using Updator.Birth;
using Updator.Common.CompressionProvider;
using Updator.Common.StorageProvider;
using Crc32 = System.IO.Hashing.Crc32;
using ZipFile = ICSharpCode.SharpZipLib.Zip.ZipFile;

Console.OutputEncoding = Encoding.UTF8;

var parsed = new Parser(a => {
   a.AllowMultiInstance = true;
   a.IgnoreUnknownArguments = true;
}).ParseArguments<Options>(args);
if (parsed.Value == null) {
   return -1;
}
var opt = parsed.Value;

if (string.IsNullOrWhiteSpace(opt.Cos)) {
   return -1;
}

// I use Tencent COS as a distributor
var cosConfig = await JsonSerializer.DeserializeAsync<TencentCosConfig>(
   new MemoryStream(Convert.FromBase64String(opt.Cos)));
var storage = new TencentCos(cosConfig);

// Get projects root from args[0]
var birthDir = opt.BirthRoot;

var config = await JsonSerializer.DeserializeAsync<BirthConfig>(
   new MemoryStream(File.ReadAllBytes(Path.Combine(birthDir, "birth.json"))));


async Task<byte[]> DecompressBrotli(byte[] data) {
   using var decompressed = new MemoryStream();
   using var compressed = new MemoryStream(data);
   var brotli = new Brotli();
   await brotli.Decompress(compressed, decompressed);
   decompressed.Position = 0;
   return decompressed.ToArray();
}

var objectKeys = new List<string>();
await Parallel.ForEachAsync(config.projects, new ParallelOptions() { MaxDegreeOfParallelism = 4 }, async (project, _) => {
   Console.WriteLine($@"Publish {project.name}");
   var path = Path.Combine(birthDir, project.name, "sources.json");
   if (!File.Exists(path)) {
      Console.WriteLine("File not found.");
      return;
   }

   using var http = new HttpClient();
   var bin = await http.GetByteArrayAsync($"https://dist-1304010062.cos.accelerate.myqcloud.com/downloader/ui-{project.platform}");
   bin = await DecompressBrotli(bin);

   using var ms = new MemoryStream();
   var zip = ZipFile.Create(ms);

   zip.StringCodec = StringCodec.FromEncoding(Encoding.UTF8);
   zip.BeginUpdate();

   var name = string.IsNullOrWhiteSpace(project.display) ? project.name : project.display;

   void AddHint(string text) {
      var textBytes = Encoding.UTF8.GetBytes(text);
      zip.SetComment(text);
      var hint = new ZipEntry(
         $"{name}/提示.txt") {
         IsUnicodeText = true,
         HostSystem = 3,
         ExternalFileAttributes = 0x81a4 << 16,
         Size = textBytes.Length,
         Crc = BitConverter.ToInt32(Crc32.Hash(textBytes))
      };
      zip.Add(new MemoryDataSource(textBytes), hint);
   }

   if (project.platform == "osx") {
      var exe = new ZipEntry(
         $"{name}/{name}{config.suffix}.zip") {
         IsUnicodeText = true,
         HostSystem = 3,
         ExternalFileAttributes = 0x81a4 << 16,
         Size = bin.Length,
         Crc = BitConverter.ToInt32(Crc32.Hash(bin))
      };
      zip.Add(new MemoryDataSource(bin), exe);
      AddHint("请解压后将所有文件移动至任意其他文件夹使用，否则可能会找不到文件！如更新过程出错，请前往 https://reito.fun 重新下载");
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
      AddHint("请解压至任意文件夹使用，不要直接在压缩包中打开！如更新过程出错，请前往 https://reito.fun 重新下载");
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
   await storage.UploadAsync($"birth/{project.name}.zip", ms);

   using var sourceMs = new MemoryStream(sources);
   await storage.UploadAsync($"{project.sourceKeyPrefix}/sources.json", sourceMs);

   objectKeys.Add($"birth/{project.name}.zip");
   objectKeys.Add($"{project.sourceKeyPrefix}/sources.json");
});

Console.WriteLine(@"Refresh CDN");
await storage.CdnPurgePath();
await storage.CdnPrefetchObjectKeys(objectKeys);

Console.WriteLine(@"Done Tencent Cos");
return 0;

namespace Updator.Birth {
   class MemoryDataSource : IStaticDataSource {
      private readonly MemoryStream _ms;

      public MemoryDataSource(byte[] b) {
         _ms = new MemoryStream(b);
      }

      public Stream GetSource() {
         return _ms;
      }
   }


   file class Options {
      [Option("cos", Required = false)]
      public string Cos { get; set; }
      [Option("path", Required = false)]
      public string BirthRoot { get; set; }
   }
}