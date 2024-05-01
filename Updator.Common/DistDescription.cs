namespace Updator.Common;

public class DistFile {
    // The object key (relative path) of file
    public string objectKey { get; set; }

    // The checksum of compressed file
    public string checksum { get; set; }

    // The file's size after compression
    public long downloadSize { get; set; }

    // The file's original size
    public long fileSize { get; set; }

    // The checksum of raw file
    public string fileChecksum { get; set; }
}

public class DistUpdateLog {
    // The build id of this update log
    public int buildId { get; set; }

    // The version string of this update log, only for display, will not be compared to anything.
    public string versionString { get; set; }

    // Localized logs, default key is _, use two letter iso name (zh, en)
    public Dictionary<string, List<string>> items { get; set; }
}

public class DistDescription {
    // Project name, for display only
    public string projectName { get; set; }

    // Version string, for display only, can be anything
    public string versionString { get; set; }

    // Incremental build id.
    public int buildId { get; set; }

    // Channel name, will be the download folder name at user side.
    // For example, `debug` will download everything to `debug` folder beside the downloader at user side.
    public string channel { get; set; }

    // Compression provider, `brotli`, `gzip`, `raw`...
    public string compression { get; set; }

    // Checksum provider, `crc64`...
    // This is separated from storage provider because at user side we do not use storage provider to download files,
    // instead we use http(s), but we still need to checksum.
    public string checksum { get; set; }

    // The executable file relative path.
    // For example `bin/program.exe`
    public string executable { get; set; }

    public string osxAppBundle { get; set; }

    // Uploaded files
    public List<DistFile> files { get; set; } = new();

    // Update logs
    public List<DistUpdateLog> updateLogs { get; set; } = new();

    public string passBuildId { get; set; }

    // Reinstall
    public List<int> reinstallBuildId { get; set; }

    public string updateLogUrl { get; set; }
    public string appIconUrl { get; set; }
}