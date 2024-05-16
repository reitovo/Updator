using System.IO.Hashing;

namespace Updator.Common.ChecksumProvider;

public class ChecksumLocalCrc64 : IChecksumProvider {
   public async Task<string> CalculateChecksum(Stream fileStream) {
      var hasher = new Crc64();
      await hasher.AppendAsync(fileStream);
      var hash = hasher.GetHashAndReset();
      return Convert.ToHexString(hash).ToLower();
   }

   class StreamChecksum : IStreamChecksum {
      private readonly Crc64 hasher = new Crc64();

      public Task<string> GetChecksum() {
         return Task.FromResult(Convert.ToHexString(hasher.GetCurrentHash()).ToLower());
      }

      public Task ProcessBlock(byte[] bytes, int count) {
         hasher.Append(bytes.AsSpan(0, count));
         return Task.CompletedTask;
      }
   }

   public IStreamChecksum CreateStreamChecksum() {
      return new StreamChecksum();
   }
}