using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Updator.Common;

namespace Updator.Downloader.CLI;

public class Source {
   public bool enable { get; set; }
   public string distributionUrl { get; set; }
}

public class Sources {
   // sources.json file version
   public int version { get; set; }
   // Update this sources.json file
   public string sourcesUrl { get; set; }
   // Custom downloader update url, default is github
   public string customDownloaderUrl { get; set; }

   public List<Source> sources { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Sources))]
public partial class SourcesSerializer : JsonSerializerContext { }

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(DistDescription))]
public partial class DistDescriptionSerializer : JsonSerializerContext { }