// See https://aka.ms/new-console-template for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Updator.Common;
using Updator.Common.CompressionProvider;
using Uploader;
using Uploader.StorageProvider;

ILogger logger = LoggerFactory.Create(builder => {
   builder.AddSimpleConsole(o => {
      o.IncludeScopes = true;
      o.TimestampFormat = "HH:mm:ss ";
      o.SingleLine = true;
   });
   builder.AddFile("./uploader.log");
   builder.SetMinimumLevel(LogLevel.Trace);
}).CreateLogger("Uploader");

var configPath = "./config.json";
if (args.Length != 0) {
   if (File.Exists(args[0])) {
      configPath = args[0];
      logger.LogInformation($"Using config file: {configPath}");
   } else {
      logger.LogError("Specified path is not a config file");
      return;
   }
} else {
   if (!File.Exists(configPath)) {
      File.WriteAllText(configPath, JsonSerializer.Serialize(new Config(), new JsonSerializerOptions() {
         DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
         WriteIndented = true
      }));
      logger.LogInformation($"Config file not found, writing a default one.");
      return;
   }
}

var config = JsonDocument.Parse(File.ReadAllBytes(configPath)).Deserialize<Config>();

logger.LogInformation($"Storage Provider: {config.storage}");
IStorageProvider storage = config.storage switch {
   "cos" => new TencentCos(config.cos),
   _ => null
};
if (storage == null) {
   logger.LogError("No effective storage provider");
   return;
}

ICompressionProvider compress = config.compression switch {
   "brotli" => new Brotli(),
   "gzip" => new GZip(),
   _ => new Raw()
};
storage.SetCompression(compress);

var root = new DistScanner(config.distributionRoot, config.ignored, logger);
logger.LogInformation($"Distribution Root: ${root.RootFullName}");
root.Scan();

var desc = new DistDescription {
   projectName = config.projectName,
   versionString = config.versionString,
   buildId = config.buildId,
   channel = config.channel,
   executable = config.executable,
   compression = config.compression
};

if (config.autoIncreaseBuildId) {
   logger.LogInformation("Auto updating build id");
   var storageDesc = new MemoryStream();
   await storage.DownloadAsync("__description.json", storageDesc);
   if (storageDesc.Length != 0) {
      var descJson = JsonDocument.Parse(storageDesc.ToArray()).Deserialize<DistDescription>();
      desc.buildId = descJson.buildId + 1;
      logger.LogInformation($"Successfully increased build id {desc.buildId}");
   } else {
      logger.LogWarning("Failed to get storage description");
   }
}

await Parallel.ForEachAsync(root.Items, async (item, _) => {
   var checksum = await storage.CalculateChecksum(item.fileInfo.OpenRead());

   desc.files.Add(new() {
      checksum = checksum,
      objectKey = item.storageObjectKey
   });

   var same = await storage.CheckSameAsync(item.storageObjectKey, checksum);
   logger.LogTrace($"Checking {item.storageObjectKey} -> {(same ? "same" : "not same")}");
   if (!same) {
      logger.LogTrace($"Uploading {item.storageObjectKey}");
      await storage.UploadAsync(item.storageObjectKey, item.fileInfo.OpenRead());
      logger.LogTrace($"Uploaded {item.storageObjectKey} -> done");
   }
});

var descText = JsonSerializer.Serialize(desc, new JsonSerializerOptions() {
   DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
   WriteIndented = true
});
await storage.UploadAsync("__description.json", new MemoryStream(Encoding.UTF8.GetBytes(descText)));
logger.LogInformation("Uploaded storage description");

logger.LogInformation("Done");