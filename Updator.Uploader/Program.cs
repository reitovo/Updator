// See https://aka.ms/new-console-template for more information

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
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

var root = new DistScanner(config.distributionRoot, config.ignored, logger);
logger.LogInformation($"Distribution Root: ${root.RootFullName}");
root.Scan();

foreach (var item in root.Items) {
   var same = await storage.CheckSameAsync(item.storageObjectKey, item.fileInfo.OpenRead());
   logger.LogTrace($"Checking {item.storageObjectKey} -> {(same ? "same" : "not same")}");
   if (!same) {
      logger.LogTrace($"Uploading {item.storageObjectKey}");
      await storage.UploadAsync(item.storageObjectKey, item.fileInfo.OpenRead());
      logger.LogTrace($"Uploaded {item.storageObjectKey}");
   }
}

