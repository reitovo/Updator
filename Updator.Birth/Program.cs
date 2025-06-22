// See https://aka.ms/new-console-template for more information

using System.Text;
using System.Text.Json;
using CommandLine;
using ICSharpCode.SharpZipLib.Zip;
using Updator.Birth;
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
var objectKeys = new List<string>();

// Parse project configuration from JSON
var projectInfo = JsonSerializer.Deserialize<ProjectInfo>(opt.ProjectConfig);
Console.WriteLine($"Processing project: {projectInfo.name}");

var project = new BirthProject {
   name = projectInfo.name,
   platform = projectInfo.platform,
   display = projectInfo.display,
   sourceKeyPrefix = projectInfo.sourceKeyPrefix
};

await Task.Run(async () => {
   Console.WriteLine($@"Publish {project.name}");
   var path = Path.Combine(birthDir, project.name, "sources.json");
   if (!File.Exists(path)) {
      Console.WriteLine("File not found.");
      return;
   }

   // Use provided executable path
   var bin = await File.ReadAllBytesAsync(opt.ExecutablePath);
   Console.WriteLine($@"Using executable: {opt.ExecutablePath}");

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
         $"{name}/{name}{projectInfo.suffix}.zip") {
         IsUnicodeText = true,
         HostSystem = 3,
         ExternalFileAttributes = 0x81a4 << 16,
         Size = bin.Length,
         Crc = BitConverter.ToInt32(Crc32.Hash(bin))
      };
      zip.Add(new MemoryDataSource(bin), exe);
      AddHint("如更新过程出错，请前往 https://reito.fun 重新下载");
   } else {
      var exe = new ZipEntry(
         $"{name}/{name}{projectInfo.suffix}{(project.platform == "win" ? ".exe" : string.Empty)}") {
         IsUnicodeText = true,
         HostSystem = 3,
         ExternalFileAttributes = 0x81ed << 16,
         Size = bin.Length,
         Crc = BitConverter.ToInt32(Crc32.Hash(bin))
      };
      zip.Add(new MemoryDataSource(bin), exe);
      AddHint("请解压至任意文件夹使用，不要直接在压缩包中打开！如更新过程出错，请前往 https://reito.fun 重新下载");
   }

   zip.CommitUpdate();
   zip.Close();

   var z = ms.ToArray();

   File.WriteAllBytes(Path.Combine(birthDir, project.name, $"{project.name}.zip"), z);

   Console.WriteLine($@"Upload Tencent Cos {project.name}");
   await storage.UploadAsync($"{project.name}.zip", ms);

   var sources = File.ReadAllBytes(path);
   using var sourceMs = new MemoryStream(sources);
   await storage.UploadAsync($"{project.sourceKeyPrefix}/sources.json", sourceMs);

   objectKeys.Add($"{project.name}.zip");
   objectKeys.Add($"{project.sourceKeyPrefix}/sources.json");
});

Console.WriteLine(@"Refresh CDN");
await storage.CdnPurgePath();
await storage.CdnPrefetchObjectKeys(objectKeys);

Console.WriteLine(@"Done Tencent Cos");
return 0;

namespace Updator.Birth {
   class MemoryDataSource(byte[] b) : IStaticDataSource {
      private readonly MemoryStream _ms = new(b);

      public Stream GetSource() {
         return _ms;
      }
   }

   file class Options {
      [Option("cos", Required = false)]
      public string Cos { get; set; }
      [Option("path", Required = false)]
      public string BirthRoot { get; set; }
      [Option("projectConfig", Required = false)]
      public string ProjectConfig { get; set; }
      [Option("executable", Required = false)]
      public string ExecutablePath { get; set; }
   }

   file class ProjectInfo {
      public string name { get; set; }
      public string platform { get; set; }
      public string display { get; set; }
      public string sourceKeyPrefix { get; set; }
      public string suffix { get; set; } = "启动器";
   }
}