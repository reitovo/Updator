using System.IO.Compression;

namespace Updator.Common.CompressionProvider;

public class GZip : ICompressionProvider {
   public async Task Compress(Stream src, Stream dst) {
      var zipStream = new GZipStream(dst, CompressionLevel.Optimal, true);
      src.Position = 0;
      await src.CopyToAsync(zipStream);
      await zipStream.FlushAsync();
      await zipStream.DisposeAsync();
      zipStream.Close();
      dst.Position = 0;
   }

   public async Task Decompress(Stream src, Stream dst) {
      src.Position = 0;
      var zipStream = new GZipStream(src, CompressionMode.Decompress, true);
      await zipStream.CopyToAsync(dst);
      await zipStream.FlushAsync();
      await zipStream.DisposeAsync();
      zipStream.Close();
      dst.Position = 0;
   }
}