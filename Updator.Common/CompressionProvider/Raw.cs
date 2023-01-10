namespace Updator.Common.CompressionProvider;

public class Raw : ICompressionProvider {
   public async Task Compress(Stream src, Stream dst) {
      await src.CopyToAsync(dst);
   }

   public async Task Decompress(Stream src, Stream dst) {
      await src.CopyToAsync(dst);
   }
}