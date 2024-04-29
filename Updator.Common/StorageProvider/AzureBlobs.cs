using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;

namespace Updator.Common.StorageProvider;

// Config reads from config.json
public class AzureBlobsConfig {
    // Connection String
    public string connectionString { get; set; }

    // Blob Container Name
    public string blobContainer { get; set; }

    // Folder
    public string objectKeyPrefix { get; set; }
}

public class AzureBlobs : IStorageProvider, ICdnRefresh {
    private BlobServiceClient _client;
    private BlobContainerClient _container;
    private string _prefix;

    public AzureBlobs(AzureBlobsConfig config) {
        _client = new BlobServiceClient(config.connectionString);
        _container = _client.GetBlobContainerClient(config.blobContainer);
        _prefix = config.objectKeyPrefix;
    }

    public async Task UploadAsync(string objectKey, Stream fileStream, Action<(long Done, long Total)> progress = null) {
        await _container.GetBlobClient($"{_prefix}{objectKey}").UploadAsync(fileStream, new BlobUploadOptions() {
            Conditions = null,
            ProgressHandler = new Progress<long>(v => { progress?.Invoke((v, fileStream.Length)); })
        });
    }

    public async Task DownloadAsync(string objectKey, Stream fileStream, Action<(long Done, long Total)> progress = null) {
        var cli = _container.GetBlobClient($"{_prefix}{objectKey}");
        if (await cli.ExistsAsync()) {
            var prop = await cli.GetPropertiesAsync();
            if (prop.HasValue) {
                await cli.DownloadToAsync(fileStream, new BlobDownloadToOptions() {
                    ProgressHandler = new Progress<long>(v => { progress?.Invoke((v, prop.Value.ContentLength)); })
                });
            }
        }
    }

    public async Task<bool> CheckSameAsync(string objectKey, string checksum) {
        var cli = _container.GetBlobClient($"{_prefix}{objectKey}");
        if (!await cli.ExistsAsync())
            return false;

        var props = await cli.GetPropertiesAsync();
        if (!props.HasValue) {
            return false;
        }

        var hash = Convert.ToHexString(props.Value.ContentHash).ToLower();
        return hash == checksum;
    }

    public Task CdnPrefetchObjectKeys(IEnumerable<string> objectKeys) {
        return Task.CompletedTask;
    }

    public Task CdnPurgePath() {
        return Task.CompletedTask;
    }
}