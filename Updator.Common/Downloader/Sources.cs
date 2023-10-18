using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Updator.Common;

namespace Updator.Downloader.CLI;

public class Source {
   public string id { get; set; }
   // Only one source should be enabled.
   public bool enable { get; set; }
   // The distribution root url of the source.
   // For example, if uploaded to tencent-cos, and the bucket has cdn cname `dist.reito.fun`, 
   // the `objectKeyPrefix` of cos is `bliveassist/release`. Then the url is `https://dist.reito.fun/bliveassist/release`
   // Because the `__description.json` is there. (`https://dist.reito.fun/bliveassist/release/__description.json`)
   public string distributionUrl { get; set; }

   public string defaultName { get; set; }
}

public class Sources {
   // sources.json file version
   public int version { get; set; }
   // Update this sources.json file from this url
   public string sourcesUrl { get; set; }
   // Custom downloader update url, default is github, check code.
   public string customDownloaderUrl { get; set; }
   // Disable auto update sources.json
   public bool disableSourcesUpdate { get; set; }
   public string defaultName { get; set; }

   // Set the default source id
   public string defaultSourceId { get; set; }
   // Available sources, you can predefine `release`, `debug` channels and let user to enable one of them
   // if the user wants to switch channel.
   public List<Source> sources { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof( Sources ))]
public partial class SourcesSerializer : JsonSerializerContext {
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof( DistDescription ))]
public partial class DistDescriptionSerializer : JsonSerializerContext {
}
