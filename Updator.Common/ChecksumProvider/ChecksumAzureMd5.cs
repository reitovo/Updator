using System.Security.Cryptography;

namespace Updator.Common.ChecksumProvider;

public class ChecksumAzureMd5 : IChecksumProvider {
    public async Task<string> CalculateChecksum(Stream fileStream) {
        var data = await MD5.HashDataAsync(fileStream);
        return Convert.ToHexString(data).ToLower();
    }
}