using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Cdn;
using Azure.ResourceManager.Cdn.Models;
using Azure.ResourceManager.FrontDoor;
using Azure.ResourceManager.FrontDoor.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;

namespace Updator.Common.StorageProvider;

// Config reads from config.json
public class AzureBlobsConfig {
    public class AzureCredential {
        public string tenantId { get; set; }
        public string clientId { get; set; }
        public string clientSecret { get; set; }
    }

    // Connection String
    public string connectionString { get; set; }

    // Blob Container Name
    public string blobContainer { get; set; }

    // Folder
    public string objectKeyPrefix { get; set; }

    // Credential
    public AzureCredential azureCredential { get; set; }

    // CDN
    public string frontDoorEndpointResourceId { get; set; }
}

public class AzureBlobs : IStorageProvider, ICdnRefresh {
    private BlobServiceClient _client;
    private BlobContainerClient _container;
    private string _prefix;

    private ArmClient _armClient;
    private ProfileResource _profile;
    private FrontDoorEndpointResource _endpoint;

    public AzureBlobs(AzureBlobsConfig config) {
        _client = new BlobServiceClient(config.connectionString);
        _container = _client.GetBlobContainerClient(config.blobContainer);
        _prefix = config.objectKeyPrefix;

        if (config.azureCredential != null) {
            var azure = config.azureCredential;
            _armClient = new ArmClient(new ClientSecretCredential(azure.tenantId, azure.clientId, azure.clientSecret));
            if (config.frontDoorEndpointResourceId != null) {
                _endpoint = _armClient.GetFrontDoorEndpointResource(ResourceIdentifier.Parse(config.frontDoorEndpointResourceId));
            }
        }
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

    public async Task CdnPurgePath() {
        if (_endpoint == null)
            return;

        await _endpoint.PurgeContentAsync(WaitUntil.Completed, new FrontDoorPurgeContent([$"/{_prefix}*"]));
    }
}