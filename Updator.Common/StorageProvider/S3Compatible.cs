using System.Diagnostics;
using System.Security.Cryptography;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

namespace Updator.Common.StorageProvider;

// Config reads from config.json
public class S3CompatibleConfig {
    // S3 Endpoint (e.g., https://s3.amazonaws.com or custom endpoint for MinIO/Wasabi/etc)
    public string endpoint { get; set; }
    
    // S3 Region (e.g., us-east-1)
    public string region { get; set; }
    
    // Access Key ID
    public string accessKeyId { get; set; }
    
    // Secret Access Key
    public string secretAccessKey { get; set; }
    
    // Bucket name
    public string bucket { get; set; }
    
    // Object key prefix (folder path)
    public string objectKeyPrefix { get; set; }
    
    // Force path style (required for MinIO and some S3-compatible services)
    public bool forcePathStyle { get; set; } = true;
    
    // Use HTTP instead of HTTPS (for local MinIO testing)
    public bool useHttp { get; set; } = false;
}

// S3 Compatible Storage Provider
public class S3Compatible : IStorageProvider {
    private readonly S3CompatibleConfig _config;
    private readonly IAmazonS3 _client;
    private readonly string _prefix;

    public S3Compatible(S3CompatibleConfig config) {
        _config = config;
        _prefix = config.objectKeyPrefix;

        var s3Config = new AmazonS3Config {
            ForcePathStyle = config.forcePathStyle,
            UseHttp = config.useHttp
        };

        // Set endpoint if specified (custom endpoint takes priority)
        if (!string.IsNullOrWhiteSpace(config.endpoint)) {
            s3Config.ServiceURL = config.endpoint;
            // When using custom endpoint, don't set RegionEndpoint to avoid conflicts
            // But we need to set AuthenticationRegion for signing
            if (!string.IsNullOrWhiteSpace(config.region)) {
                s3Config.AuthenticationRegion = config.region;
            }
        } else if (!string.IsNullOrWhiteSpace(config.region)) {
            // Only set RegionEndpoint when using standard AWS endpoints
            s3Config.RegionEndpoint = RegionEndpoint.GetBySystemName(config.region);
        }

        _client = new AmazonS3Client(
            config.accessKeyId,
            config.secretAccessKey,
            s3Config
        );
    }

    public async Task UploadAsync(string objectKey, Stream fileStream, Action<(long Done, long Total)> progress = null) {
        var key = Path.Combine(_prefix, objectKey).Replace(@"\", "/");
        
        try {
            var transferUtility = new TransferUtility(_client);
            var uploadRequest = new TransferUtilityUploadRequest {
                BucketName = _config.bucket,
                Key = key,
                InputStream = fileStream
            };

            if (progress != null) {
                uploadRequest.UploadProgressEvent += (sender, args) => {
                    progress.Invoke((args.TransferredBytes, args.TotalBytes));
                };
            }

            await transferUtility.UploadAsync(uploadRequest);
            Debug.WriteLine($"Uploaded {key} successfully");
        } catch (AmazonS3Exception ex) {
            Debug.WriteLine($"S3 Upload Error: {ex.Message}");
            throw;
        } catch (Exception ex) {
            Debug.WriteLine($"Upload Error: {ex.Message}");
            throw;
        }
    }

    public async Task DownloadAsync(string objectKey, Stream fileStream, Action<(long Done, long Total)> progress = null) {
        var key = Path.Combine(_prefix, objectKey).Replace(@"\", "/");
        
        try {
            var request = new GetObjectRequest {
                BucketName = _config.bucket,
                Key = key
            };

            using var response = await _client.GetObjectAsync(request);
            var totalBytes = response.ContentLength;
            long transferredBytes = 0;

            var buffer = new byte[81920]; // 80 KB buffer
            int bytesRead;

            while ((bytesRead = await response.ResponseStream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                transferredBytes += bytesRead;
                progress?.Invoke((transferredBytes, totalBytes));
            }

            Debug.WriteLine($"Downloaded {key} successfully");
        } catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) {
            Debug.WriteLine($"Object {key} not found");
            // Don't throw, just return (similar to other providers)
        } catch (AmazonS3Exception ex) {
            Debug.WriteLine($"S3 Download Error: {ex.Message}");
            throw;
        } catch (Exception ex) {
            Debug.WriteLine($"Download Error: {ex.Message}");
            throw;
        }
    }

    public async Task<bool> CheckSameAsync(string objectKey, string checksum) {
        var key = Path.Combine(_prefix, objectKey).Replace(@"\", "/");
        
        try {
            var request = new GetObjectMetadataRequest {
                BucketName = _config.bucket,
                Key = key
            };

            var response = await _client.GetObjectMetadataAsync(request);
            
            // Get ETag (which is MD5 for single-part uploads)
            var etag = response.ETag.Trim('"');
            
            // Try to get custom metadata for CRC64 if stored
            if (response.Metadata["x-amz-meta-crc64"] != null) {
                var crc64 = response.Metadata["x-amz-meta-crc64"];
                return crc64 == checksum;
            }
            
            // For MD5 comparison
            if (etag.Length == 32) {
                // Simple upload, ETag is MD5
                return etag.ToLower() == checksum.ToLower();
            }
            
            // For multipart uploads, we need to download and calculate
            // Or use custom metadata. For now, assume different if not simple MD5.
            Debug.WriteLine($"Cannot verify checksum for {key}: ETag is multipart ({etag})");
            return false;
        } catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) {
            Debug.WriteLine($"Object {key} not found");
            return false;
        } catch (AmazonS3Exception ex) {
            Debug.WriteLine($"S3 CheckSame Error: {ex.Message}");
            return false;
        } catch (Exception ex) {
            Debug.WriteLine($"CheckSame Error: {ex.Message}");
            return false;
        }
    }
}

