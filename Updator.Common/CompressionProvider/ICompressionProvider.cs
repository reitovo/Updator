namespace Updator.Common.CompressionProvider;

// Provide compress / decompress ability
public interface ICompressionProvider {
    Task Compress(Stream src, Stream dst);
    Task Decompress(Stream src, Stream dst);

    IStreamCompression CreateCompressStream(Stream dest);
    IStreamCompression CreateDecompressStream(Stream dest);
}

public interface IStreamCompression : IDisposable {
    Task ProcessBlock(byte[] bytes);
    Task FlushAsync();
}