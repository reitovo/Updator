using CosCrc64 = COSXML.Utils.Crc64;

namespace Updator.Common.ChecksumProvider;

// Tencent COS SDK has CRC64 util built-in, so just use it.
public class ChecksumCosCrc64 : IChecksumProvider {
    static ChecksumCosCrc64() {
        CosCrc64.InitECMA();
    }

    public async Task<string> CalculateChecksum(Stream fileStream) {
        ulong crc1 = 0;
        int num;
        byte[] numArray = new byte[2048];
        while ((num = await fileStream.ReadAsync(numArray)) > 0) {
            ulong crc2 = CosCrc64.Compute(numArray, 0, num);
            crc1 = crc1 != 0UL ? CosCrc64.Combine(crc1, crc2, (long)num) : crc2;
        }

        return crc1.ToString();
    }
}