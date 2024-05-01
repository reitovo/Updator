namespace Updator.Common.ChecksumProvider;

public interface IChecksumProvider {
    // Calculate whatever checksum into string for further comparison.
    Task<string> CalculateChecksum(Stream fileStream);

    IStreamChecksum CreateStreamChecksum();
}

public interface IStreamChecksum {
    Task<string> GetChecksum();
    Task ProcessBlock(byte[] bytes, int count);
}