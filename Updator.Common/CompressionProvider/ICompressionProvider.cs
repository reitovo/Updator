namespace Updator.Common.CompressionProvider;

// Provide compress / decompress ability
public interface ICompressionProvider {
   Task Compress(Stream src, Stream dst);
   Task Decompress(Stream src, Stream dst);
}
