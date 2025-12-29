using Updator.Common.StorageProvider;

namespace Updator.Downloader.Publish;

public class Config {
   public string githubToken { get; set; }
   public string storageType { get; set; } = "cos"; // "cos", "azure", "s3"
   public TencentCosConfig cos { get; set; }
   public AzureBlobsConfig azure { get; set; }
   public S3CompatibleConfig s3 { get; set; }
}