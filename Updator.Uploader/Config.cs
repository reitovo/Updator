using Uploader.StorageProvider;

namespace Uploader;

public class Config {
   // The project name
   public string projectName { get; set; } = "Default Project Name";
   // The files root to distribute
   public string distributionRoot { get; set; } = "./dist";
   // The version string to set
   public string versionString { get; set; } = "0.0.1";
   // The build id to set
   public int buildId { get; set; } = 100;
   // If set, ignore build id and auto increase uploaded
   public bool autoIncreaseBuildId { get; set; } = true;
   // Channel name
   public string channel { get; set; }

   // Files / directories to ignore in object key format
   public string[] ignored { get; set; } = Array.Empty<string>();

   // The name of storage provider
   public string storage { get; set; } = "cos";
   // tencent cos config ("cos")
   public TencentCosConfig cos { get; set; }
}