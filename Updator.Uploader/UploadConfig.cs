using Updator.Common;
using Updator.Common.StorageProvider;

namespace Updator.Uploader;

public class UploadConfig {
   // The project name, display at user side.
   public string projectName { get; set; } = "Default Project Name";
   // The files root to distribute
   public string distributionRoot { get; set; } = "./dist";
   // The version string to set
   public string versionString { get; set; } = "0.0.1";
   // The build id to set, should be incremental
   public int buildId { get; set; } = 100;
   // If set, it will auto increase build id. For example 12900 -> 12901 -> 12902
   // However if you manually set build id larger than uploaded id (13000 > 12902)
   // It will use 13000 instead.
   public bool autoIncreaseBuildId { get; set; } = true;
   // Channel name, will be the download folder name at user side.
   // For example, `debug` will download everything to `debug` folder beside the downloader at user side.
   public string channel { get; set; }

   // Update logs
   public List<DistUpdateLog> updateLogs { get; set; } = new();

   // Files / directories to ignore in object key format
   // Directories should be ended with `/` (`bin/`, `crash/`)
   public string[] ignored { get; set; } = Array.Empty<string>();
   // Compression methods
   public string compression { get; set; } = "brotli";
   // The execueable relative path (`bin/program.exe`)
   public string executable { get; set; }
   // Checksum provider
   public string checksum { get; set; } = "crc64";
   // The name of storage provider
   public string storage { get; set; } = "cos";
   // tencent cos config ("cos")
   public TencentCosConfig cos { get; set; }
   // azure storage blobs config ("azure")
   public AzureBlobsConfig azure { get; set; }
   // s3 compatible storage config ("s3")
   public S3CompatibleConfig s3 { get; set; }

   // pass build id to executable as argument --<passBuildId>
   public string passBuildId { get; set; }

   // If upgrading from older version than any of these, delete all before download
   public List<int> reinstallBuildId { get; set; }
   public string updateLogUrl { get; set; }
   public string appIconUrl { get; set; }
}