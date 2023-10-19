using Uploader.StorageProvider;

namespace Updator.Birth;

public class BirthProject {
   public string name { get; set; }
   public string platform { get; set; }
   public string display { get; set; }
   public string downloaderUrl { get; set; }
}

public class BirthConfig {
   public string suffix { get; set; }
   public TencentCosConfig cos { get; set; }
   public List<BirthProject> projects { get; set; }
}