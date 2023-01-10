namespace Updator.Common;

public class DistFile
{
    public string objectKey { get; set; }
    public string checksum { get; set; }
}

public class DistDescription
{
    public string projectName { get; set; }
    public string versionString { get; set; }
    public int buildId { get; set; }
    public string channel { get; set; }
    public string compression { get; set; }
    public string checksum { get; set; }
    public string executable { get; set; }
    public List<DistFile> files { get; set; } = new();
}