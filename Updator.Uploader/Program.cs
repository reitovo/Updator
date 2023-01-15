﻿// See https://aka.ms/new-console-template for more information

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandLine;
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

var parsed = Parser.Default.ParseArguments<Options>(args);
var options = parsed.Value;

// Reads config.json 
var configString = string.Empty;
var configPath = string.Empty;

if (!string.IsNullOrWhiteSpace(options.ConfigFile)) {
   if (File.Exists(options.ConfigFile)) {
      logger.LogInformation($"Using config file: {options.ConfigFile}");
      configString = File.ReadAllText(options.ConfigFile);
      configPath = options.ConfigFile;
   } else {
      logger.LogError("Specified path is not a config file");
      return;
   }
}

if (!string.IsNullOrWhiteSpace(options.Base64ConfigFile)) {
   logger.LogInformation($"Using base64 config file");
   configString = Encoding.UTF8.GetString(Convert.FromBase64String(options.Base64ConfigFile));
}

if (!File.Exists("./config.json")) {
   File.WriteAllText("./config.json", JsonSerializer.Serialize(new Config(), new JsonSerializerOptions() {
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
      WriteIndented = true
   }));
   logger.LogInformation($"Config file not found, writing a default one.");
   return;
}

if (string.IsNullOrWhiteSpace(configString)) {
   configString = File.ReadAllText("./config.json");
   configPath = "./config.json";
}

var config = JsonDocument.Parse(configString).Deserialize<Config>();

if (!string.IsNullOrWhiteSpace(options.DistributionRoot)) {
   logger.LogInformation($"Overwrite distributionRoot");
   config.distributionRoot = options.DistributionRoot;
}

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
            config.buildId = desc.buildId;
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

// Write logs if there's
if (options.UpdateLogs != null) {
   var updateLogs = options.UpdateLogs.ToList();
   if (updateLogs.Any()) {
      logger.LogInformation($"Writing update logs.");
      var updateLog = new DistUpdateLog() {
         buildId = desc.buildId,
         items = new() {
            {"_", updateLogs}
         },
         versionString = desc.versionString
      };
      desc.updateLogs.Add(updateLog);
      config.updateLogs.Add(updateLog);
   }
}

if (!string.IsNullOrWhiteSpace(configPath) && !options.NoWriteBack) {
   logger.LogInformation($"Save update logs to config.json");
   var configText = JsonSerializer.Serialize(config, new JsonSerializerOptions() {
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
      WriteIndented = true
   });
   File.WriteAllText(configPath, configText);
}

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

file class Options {
   [Option("config", Required = false, HelpText = "Config file to be processed.")]
   public string ConfigFile { get; set; }

   [Option("base64", Required = false, HelpText = "Base64 encoded config file to be processed.")]
   public string Base64ConfigFile { get; set; }

   [Option("distribution-root", Required = false, HelpText = "Override `distributionRoot`.")]
   public string DistributionRoot { get; set; }

   [Option("update-log", Required = false,
      HelpText = "Add a line to update log in (set or auto-increased) `buildId` and current `versionString`.")]
   public IEnumerable<string> UpdateLogs { get; set; }

   [Option("no-write-back", Required = false, Default = false,
      HelpText = "Disable write updated config.json back to file")]
   public bool NoWriteBack { get; set; }
}