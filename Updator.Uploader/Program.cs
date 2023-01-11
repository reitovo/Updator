// See https://aka.ms/new-console-template for more information

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Updator.Common;
using Updator.Common.ChecksumProvider;
using Updator.Common.CompressionProvider;
using Uploader;
using Uploader.StorageProvider;

// Initialize logger
ILogger logger = LoggerFactory.Create(builder => {
   builder.AddSimpleConsole(o => {
      o.IncludeScopes = true;
      o.TimestampFormat = "HH:mm:ss ";
      o.SingleLine = true;
   });
   builder.AddFile("./uploader.log");
   builder.SetMinimumLevel(LogLevel.Trace);
}).CreateLogger("Uploader");

// Reads config.json
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

// Initialize providers
logger.LogInformation($"Providers: {config.storage} {config.checksum} {config.compression}");
IStorageProvider storage = config.storage switch {
   "cos" => new TencentCos(config.cos),
   _ => null
};
if (storage == null) {
   logger.LogError("No effective storage provider");
   return;
}

IChecksumProvider check = config.checksum switch {
   "crc64" => new Crc64(),
   _ => null
};
if (check == null) {
   logger.LogError("No effective checksum provider");
   return;
}

ICompressionProvider compress = config.compression switch {
   "brotli" => new Brotli(),
   "gzip" => new GZip(),
   _ => new Raw()
};

// Scan files in the folder
var root = new DistScanner(config.distributionRoot, config.ignored, logger);
logger.LogInformation($"Distribution Root: ${root.RootFullName}");
root.Scan();

// Create description
var desc = new DistDescription {
   projectName = config.projectName,
   versionString = config.versionString,
   buildId = config.buildId,
   channel = config.channel,
   executable = config.executable,
   compression = config.compression,
   checksum = config.checksum,
   updateLogs = config.updateLogs
};

// Check old description
var compressionMismatch = true;
var storageDesc = new MemoryStream();
await storage.DownloadAsync("__description.json", storageDesc);
if (storageDesc.Length != 0) {
   try {
      var oldDesc = JsonDocument.Parse(storageDesc.ToArray()).Deserialize<DistDescription>();
      if (config.autoIncreaseBuildId) {
         logger.LogInformation("Auto updating build id");
         if (desc.buildId > oldDesc.buildId) {
            logger.LogInformation(
               $"Forced build id {desc.buildId} because it is larger than existing {oldDesc.buildId}");
         } else {
            desc.buildId = oldDesc.buildId + 1;
            logger.LogInformation($"Successfully increased build id {desc.buildId}");
         }
      }
      // If compression methods are same, it can skip re-upload
      if (oldDesc.compression == desc.compression) {
         compressionMismatch = false;
         logger.LogInformation($"Compression type match");
      }
   } catch (Exception) {
      // ignored
   }
} else {
   logger.LogWarning("Failed to get storage description");
}

// Upload files concurrently.
ConcurrentBag<string> uploadedObjectKeys = new();
await Parallel.ForEachAsync(root.Items, async (item, _) => {
   using var ms = new MemoryStream();
   var fs = item.fileInfo.OpenRead();
   await compress.Compress(fs, ms);
   await fs.DisposeAsync();
   fs.Close();
   ms.Position = 0;
   var checksum = await check.CalculateChecksum(ms);

   desc.files.Add(new() {
      checksum = checksum,
      objectKey = item.storageObjectKey
   });

   var upload = false;

   // Check if need re-upload
   if (compressionMismatch) {
      upload = true;
   } else {
      var same = await storage.CheckSameAsync(item.storageObjectKey, checksum);
      logger.LogTrace($"Checking {item.storageObjectKey} -> {(same ? "same" : "not same")}");
      if (!same) {
         upload = true;
      }
   }

   if (upload) {
      logger.LogTrace($"Uploading {item.storageObjectKey}");
      ms.Position = 0;
      await storage.UploadAsync(item.storageObjectKey, ms);
      logger.LogTrace($"Uploaded {item.storageObjectKey} -> done");
      uploadedObjectKeys.Add(item.storageObjectKey);
   }
});

// Write description
var descText = JsonSerializer.Serialize(desc, new JsonSerializerOptions() {
   DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
   WriteIndented = true
});
await storage.UploadAsync("__description.json", new MemoryStream(Encoding.UTF8.GetBytes(descText)));
uploadedObjectKeys.Add("__description.json");
logger.LogInformation("Uploaded storage description");

// Refresh CDN if provider has such interface
if (storage is ICdnRefresh cdn) {
   logger.LogInformation("Refresh CDN");
   await cdn.RefreshObjectKeys(uploadedObjectKeys);
}

logger.LogInformation("Done");