using System.Diagnostics;
using Updator.Common.ChecksumProvider;
using Updator.Common.CompressionProvider;

namespace Updator.Common.Utils;

public static class HttpClientExtension {
   public static async Task DownloadAsync(this HttpClient client, string url, FileInfo outFile, IChecksumProvider checksum, string checksumValue,
      ICompressionProvider compress, CancellationToken cancellationToken, Action<(long BlockRead, long TotalRead, long Total)> progress = null) {
      if (outFile.Exists)
         outFile.Delete();

      await using var fs = outFile.Create();

      var buffer = new byte[16384];
      var check = checksum.CreateStreamChecksum();
      using var decompress = compress.CreateDecompressStream(fs);

      var bytesRead = 0L;
      var request = new HttpRequestMessage {
         RequestUri = new Uri(url),
         Method = HttpMethod.Get
      };
      var resp = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
      if (!resp.IsSuccessStatusCode) {
         throw new InvalidDataException($"unsuccessful status code {resp.StatusCode}");
      }

      var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
      var contentLength = resp.Content.Headers.ContentLength!.Value;
      while (bytesRead < contentLength) {
         var read = await stream.ReadAsync(buffer, cancellationToken);
         Debug.WriteLine($"{url} read {read}");

         if (read == 0)
            continue;

         await check.ProcessBlock(buffer, read);
         await decompress.ProcessBlock(buffer[..read]);

         bytesRead += read;
         progress?.Invoke((read, bytesRead, contentLength));
      }

      await decompress.FlushAsync();

      var c = await check.GetChecksum();
      if (c != checksumValue) {
         throw new InvalidDataException($"checksum failed {c} != {checksumValue}");
      }
   }
}