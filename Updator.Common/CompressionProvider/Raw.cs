namespace Updator.Common.CompressionProvider;

// No compression
public class Raw : ICompressionProvider {
    public async Task Compress(Stream src, Stream dst) {
        await src.CopyToAsync(dst);
    }

    public async Task Decompress(Stream src, Stream dst) {
        await src.CopyToAsync(dst);
    }

    class StreamCompression(Stream stream) : IStreamCompression {
        public void Dispose() {
            stream.Dispose();
        }

        public async Task ProcessBlock(byte[] bytes) {
            await stream.WriteAsync(bytes);
        }

        public async Task FlushAsync() {
            await stream.FlushAsync();
        }
    }

    public IStreamCompression CreateCompressStream(Stream dest) {
        return new StreamCompression(dest);
    }

    public IStreamCompression CreateDecompressStream(Stream dest) {
        return new StreamCompression(dest);
    }
}