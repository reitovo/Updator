namespace Updator.Common.Utils;

public class ChunkedReadStream : Stream {
   private readonly Queue<byte[]> chunks = new();
   private long position = 0; // Current "global" position
   private long length = 0; // Total length of data

   private byte[] currentChunk;
   private int currentChunkPosition;

   public override bool CanRead => true;
   public override bool CanSeek => false;
   public override bool CanWrite => false;

   public override long Position {
      get => position;
      set => throw new NotSupportedException();
   }

   public override long Length => length;

   public void AddChunk(byte[] chunk) {
      chunks.Enqueue(chunk);
      length += chunk.Length;
   }

   public override int Read(byte[] buffer, int offset, int count) {
      if (count == 0) return 0;

      int bytesRead = 0;
      while (count > 0 && (chunks.Count > 0 || currentChunkPosition < currentChunk?.Length)) {
         if (currentChunk == null || currentChunkPosition >= currentChunk.Length) {
            currentChunk = chunks.Dequeue();
            currentChunkPosition = 0;
         }

         int bytesToCopy = Math.Min(count, currentChunk.Length - currentChunkPosition);
         Array.Copy(currentChunk, currentChunkPosition, buffer, offset, bytesToCopy);

         currentChunkPosition += bytesToCopy;
         offset += bytesToCopy;
         bytesRead += bytesToCopy;
         position += bytesToCopy;
         count -= bytesToCopy;
      }

      return bytesRead;
   }

   public override void Flush() {
      /* No implementation needed for read-only stream */
   }

   public override long Seek(long offset, SeekOrigin origin) {
      throw new NotSupportedException();
   }

   public override void SetLength(long value) {
      throw new NotSupportedException();
   }

   public override void Write(byte[] buffer, int offset, int count) {
      throw new NotSupportedException();
   }
}