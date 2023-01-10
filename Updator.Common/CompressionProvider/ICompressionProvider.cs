namespace Updator.Common.CompressionProvider;

public interface ICompressionProvider {
   Task Compress(Stream src, Stream dst);
   Task Decompress(Stream src, Stream dst);
}