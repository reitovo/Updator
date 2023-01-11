namespace Updator.Common;

public class DistFile {
   public string objectKey { get; set; }
   public string checksum { get; set; }
}

public class DistUpdateLog {
   public int buildId { get; set; }
   public string versionString { get; set; }

   // Localized, default key is _, use two letter iso name (zh, en)
   public Dictionary<string, List<string>> items { get; set; }
}

public class DistDescription {
   public string projectName { get; set; }
   public string versionString { get; set; }
   public int buildId { get; set; }
   public string channel { get; set; }
   public string compression { get; set; }
   public string checksum { get; set; }
   public string executable { get; set; }
   public List<DistFile> files { get; set; } = new();

   public List<DistUpdateLog> updateLogs { get; set; } = new();
}