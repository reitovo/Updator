namespace Updator.Common;

public class StorageFile {
   public string objectKey { get; set; }
   public string checksum { get; set; }
}

public class StorageDescription {
   public string projectName { get; set; }
   public string versionString { get; set; }
   public int buildId { get; set; }
   public string channel { get; set; }
}