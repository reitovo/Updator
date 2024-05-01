using System.IO.Compression;
using Updator.Common.Utils;

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


    class StreamCompress(Stream dest) : IStreamCompression {
        private readonly BrotliStream _zip = new(dest, CompressionLevel.Optimal, true);

        public void Dispose() {
            _zip.Dispose();
        }

        public async Task ProcessBlock(byte[] bytes) {
            await _zip.WriteAsync(bytes);
        }

        public async Task FlushAsync() {
            await _zip.FlushAsync();
        }
    }

    class StreamDecompress : IStreamCompression {
        private readonly ChunkedReadStream _buffer;
        private readonly BrotliStream _zip;
        private readonly Stream _dest;

        public StreamDecompress(Stream dest) {
            _dest = dest;
            _buffer = new ChunkedReadStream();
            _zip = new BrotliStream(_buffer, CompressionMode.Decompress, true);
        }

        public void Dispose() {
            _zip.Dispose();
        }

        public async Task ProcessBlock(byte[] bytes) {
            _buffer.AddChunk(bytes);
            await _zip.CopyToAsync(_dest);
        }

        public async Task FlushAsync() {
            await _zip.CopyToAsync(_dest);
            await _dest.FlushAsync();
        }
    }

    public IStreamCompression CreateCompressStream(Stream dest) {
        return new StreamCompress(dest);
    }

    public IStreamCompression CreateDecompressStream(Stream dest) {
        return new StreamDecompress(dest);
    }
}