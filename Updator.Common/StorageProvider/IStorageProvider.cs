﻿namespace Updator.Common.StorageProvider;

// Basic ability of an storage provider
public interface IStorageProvider {
   Task UploadAsync(string objectKey, Stream fileStream, Action<(long Done, long Total)> progress = null);
   Task DownloadAsync(string objectKey, Stream fileStream, Action<(long Done, long Total)> progress = null);
   Task<bool> CheckSameAsync(string objectKey, string checksum);
}