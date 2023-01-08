namespace Uploader.StorageProvider;

public interface IStorageProvider {
   Task UploadAsync(string objectKey, Stream fileStream, Action<(long Done, long Total)> progress = null);
   Task DownloadAsync(string objectKey, Stream fileStream, Action<(long Done, long Total)> progress = null);
   Task<bool> CheckSameAsync(string objectKey, Stream fileStream);
   Task<string> CalculateChecksum(Stream fileStream);
}