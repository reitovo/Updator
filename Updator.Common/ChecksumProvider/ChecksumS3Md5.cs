using System.Security.Cryptography;

namespace Updator.Common.ChecksumProvider;

// S3 uses MD5 ETag for single-part uploads
// This is the same as Azure MD5, but kept separate for clarity
public class ChecksumS3Md5 : IChecksumProvider {
    public async Task<string> CalculateChecksum(Stream fileStream) {
        var data = await MD5.HashDataAsync(fileStream);
        return Convert.ToHexString(data).ToLower();
    }

    class StreamChecksum : IStreamChecksum {
        private readonly MD5 md5 = MD5.Create();

        public Task<string> GetChecksum() {
            md5.TransformFinalBlock([], 0, 0);
            return Task.FromResult(md5.Hash == null ? string.Empty : Convert.ToHexString(md5.Hash).ToLower());
        }

        public Task ProcessBlock(byte[] bytes, int count) {
            md5.TransformBlock(bytes, 0, count, null, 0);
            return Task.CompletedTask;
        }
    }

    public IStreamChecksum CreateStreamChecksum() {
        return new StreamChecksum();
    }
}

