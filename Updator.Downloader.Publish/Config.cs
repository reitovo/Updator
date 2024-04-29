using Updator.Common.StorageProvider;

namespace Updator.Downloader.Publish;

public class Config {
   public string githubToken { get; set; }
   public TencentCosConfig cos { get; set; }
}