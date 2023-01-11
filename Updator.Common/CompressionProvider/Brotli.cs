using System.IO.Compression;

namespace Updator.Common.CompressionProvider;

// Use brotli as compression
public class Brotli : ICompressionProvider {
   public async Task Compress(Stream src, Stream dst) {
      var zipStream = new BrotliStream(dst, CompressionLevel.Optimal, true);
      await src.CopyToAsync(zipStream);
      await zipStream.FlushAsync();
      await dst.FlushAsync();
      await zipStream.DisposeAsync();
      zipStream.Close();
   }

   public async Task Decompress(Stream src, Stream dst) {
      var zipStream = new BrotliStream(src, CompressionMode.Decompress, true);
      await zipStream.CopyToAsync(dst);
      await zipStream.FlushAsync();
      await dst.FlushAsync();
      await zipStream.DisposeAsync();
      zipStream.Close();
   }
}