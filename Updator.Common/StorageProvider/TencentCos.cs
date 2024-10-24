﻿using System.Diagnostics;
using COSXML;
using COSXML.Auth;
using COSXML.Model.Object;
using TencentCloud.Cdn.V20180606;
using TencentCloud.Cdn.V20180606.Models;
using TencentCloud.Common;
using TencentCloud.Common.Profile;
using TencentCloud.Teo.V20220901;
using TencentCloud.Teo.V20220901.Models;
using DescribePurgeTasksRequest = TencentCloud.Teo.V20220901.Models.DescribePurgeTasksRequest;
using Task = System.Threading.Tasks.Task;

namespace Updator.Common.StorageProvider;

// Config reads from config.json
public class TencentCosConfig {
   // Region
   public string region { get; set; }

   // Secret id, https://console.cloud.tencent.com/cam/capi
   public string secretId { get; set; }

   // Secret key, https://console.cloud.tencent.com/cam/capi
   public string secretKey { get; set; }

   // The bucket to upload to
   public string bucket { get; set; }

   // Add a prefix to all objects. Put everything in an sub-folder to reuse the bucket
   // Combined as Path.Combine(config.objectKeyPrefix, objectKey).Replace(@"\", "/")
   // For example `project-name/release`
   public string objectKeyPrefix { get; set; }

   // If you need refresh cdn, set root url
   // For example `https://dist.reito.fun/project-name/release`
   public string cdnRefreshRoot { get; set; }

   // If you need refresh cdn directory
   public string cdnRefreshPath { get; set; }
   public bool useEdgeOne { get; set; }
   public string edgeOneZoneId { get; set; }
}

// Tencent COS as storage, it supports CDN refresh
public class TencentCos : IStorageProvider, ICdnRefresh {
   private TencentCosConfig _config;
   private CosXml _cosXml;

   public TencentCos(TencentCosConfig config) {
      _config = config;

      CosXmlConfig c = new CosXmlConfig.Builder()
         .SetRegion(config.region) // 设置默认的区域, COS 地域的简称请参照 https://cloud.tencent.com/document/product/436/6224  
         .SetConnectionTimeoutMs(500000)
         .SetReadWriteTimeoutMs(500000)
         .SetEndpointSuffix("cos.accelerate.myqcloud.com")
         .Build();

      string secretId = config.secretId; // 云 API 密钥 SecretId, 获取 API 密钥请参照 https://console.cloud.tencent.com/cam/capi
      string
         secretKey = config.secretKey; // 云 API 密钥 SecretKey, 获取 API 密钥请参照 https://console.cloud.tencent.com/cam/capi
      long durationSecond = 600; //每次请求签名有效时长，单位为秒
      QCloudCredentialProvider qCloudCredentialProvider = new DefaultQCloudCredentialProvider(secretId,
         secretKey, durationSecond);

      _cosXml = new CosXmlServer(c, qCloudCredentialProvider);
   }

   public async Task UploadAsync(string objectKey, Stream fileStream, Action<(long Done, long Total)> progress = null) {
      await Task.Run(() => {
         var retry = 3;
         while (retry-- > 0) {
            try {
               string bucket = _config.bucket; //存储桶，格式：BucketName-APPID
               string key = Path.Combine(_config.objectKeyPrefix, objectKey).Replace(@"\", "/");
               PutObjectRequest request = new PutObjectRequest(bucket, key, fileStream);
               //设置进度回调
               request.SetCosProgressCallback(delegate(long completed, long total) {
                  Debug.WriteLine($"progress = {completed * 100.0 / total:##.##}%");
                  progress?.Invoke((completed, total));
               });
               //执行请求
               PutObjectResult result = _cosXml.PutObject(request);
               //打印请求结果
               Debug.WriteLine(result.GetResultInfo());
               break;
            } catch (COSXML.CosException.CosClientException clientEx) {
               //请求失败
               Debug.WriteLine("CosClientException: " + clientEx);
            } catch (COSXML.CosException.CosServerException serverEx) {
               //请求失败
               Debug.WriteLine("CosServerException: " + serverEx.GetInfo());
            }
         }
      });
   }

   public async Task DownloadAsync(string objectKey, Stream fileStream,
      Action<(long Done, long Total)> progress = null) {
      var retry = 3;
      while (retry-- > 0) {
         try {
            // 存储桶名称，此处填入格式必须为 bucketname-APPID, 其中 APPID 获取参考 https://console.cloud.tencent.com/developer
            string bucket = _config.bucket;
            string key = Path.Combine(_config.objectKeyPrefix, objectKey).Replace(@"\", "/");
            GetObjectBytesRequest request = new GetObjectBytesRequest(bucket, key);
            //设置进度回调
            request.SetCosProgressCallback(delegate(long completed, long total) {
               Debug.WriteLine($"progress = {completed * 100.0 / total:##.##}%");
               progress?.Invoke((completed, total));
            });
            //执行请求
            GetObjectBytesResult result = _cosXml.GetObject(request);
            //请求成功
            Debug.WriteLine(result.GetResultInfo());

            await fileStream.WriteAsync(result.content);
            break;
         } catch (COSXML.CosException.CosClientException clientEx) {
            //请求失败
            Debug.WriteLine("CosClientException: " + clientEx);
         } catch (COSXML.CosException.CosServerException serverEx) {
            //请求失败
            Debug.WriteLine("CosServerException: " + serverEx.GetInfo());
         } catch (Exception ex) {
            Debug.WriteLine("Download Exception: " + ex);
         }
      }
   }

   public async Task<bool> CheckSameAsync(string objectKey, string checksum) {
      return await Task.Run(() => {
         try {
            string bucket = _config.bucket; //存储桶，格式：BucketName-APPID
            string key = Path.Combine(_config.objectKeyPrefix, objectKey).Replace(@"\", "/");
            HeadObjectRequest request = new HeadObjectRequest(bucket, key);
            //执行请求
            HeadObjectResult result = _cosXml.HeadObject(request);
            //请求成功
            Debug.WriteLine(result.GetResultInfo());

            var match = checksum == result.crc64ecma;
            return match;
         } catch (COSXML.CosException.CosClientException clientEx) {
            //请求失败
            Debug.WriteLine("CosClientException: " + clientEx);
            return false;
         } catch (COSXML.CosException.CosServerException serverEx) {
            //请求失败
            Debug.WriteLine("CosServerException: " + serverEx.GetInfo());
            return false;
         }
      });
   }

   private async Task PrefetchObjectKeysCdn(IEnumerable<string> objectKeys) {
      try {
         if (!string.IsNullOrWhiteSpace(_config.cdnRefreshRoot)) {
            Credential cred = new Credential {
               SecretId = _config.secretId,
               SecretKey = _config.secretKey
            };
            // 实例化一个client选项，可选的，没有特殊需求可以跳过
            ClientProfile clientProfile = new ClientProfile();
            // 实例化一个http选项，可选的，没有特殊需求可以跳过
            HttpProfile httpProfile = new HttpProfile {
               Endpoint = ("cdn.tencentcloudapi.com")
            };
            clientProfile.HttpProfile = httpProfile;

            // 实例化要请求产品的client对象,clientProfile是可选的
            CdnClient client = new CdnClient(cred, _config.region, clientProfile);
            // 实例化一个请求对象,每个接口都会对应一个request对象
            var req = new PushUrlsCacheRequest {
               Urls = objectKeys.Select(a => Path.Combine(_config.cdnRefreshRoot, a).Replace(@"\", "/")).ToArray(),
               UrlEncode = true
            };

            if (req.Urls.Length == 0)
               return;

            // 返回的resp是一个PurgeUrlsCacheResponse的实例，与请求对象对应
            var resp = await client.PushUrlsCache(req);
            // 输出json格式的字符串回包
            Debug.WriteLine(AbstractModel.ToJsonString(resp));
         }
      } catch (Exception ex) {
         Debug.WriteLine(ex);
      }
   }

   private async Task PurgeRootCdn() {
      try {
         if (!string.IsNullOrWhiteSpace(_config.cdnRefreshPath)) {
            Credential cred = new Credential {
               SecretId = _config.secretId,
               SecretKey = _config.secretKey
            };
            // 实例化一个client选项，可选的，没有特殊需求可以跳过
            ClientProfile clientProfile = new ClientProfile();
            // 实例化一个http选项，可选的，没有特殊需求可以跳过
            HttpProfile httpProfile = new HttpProfile {
               Endpoint = ("cdn.tencentcloudapi.com")
            };
            clientProfile.HttpProfile = httpProfile;

            // 实例化要请求产品的client对象,clientProfile是可选的
            CdnClient client = new CdnClient(cred, _config.region, clientProfile);
            // 实例化一个请求对象,每个接口都会对应一个request对象 
            PurgePathCacheRequest req = new PurgePathCacheRequest() {
               Paths = new[] { _config.cdnRefreshPath },
               UrlEncode = true,
               FlushType = "flush"
            };
            if (req.Paths.Length == 0)
               return;

            // 返回的resp是一个PurgeUrlsCacheResponse的实例，与请求对象对应
            var resp = await client.PurgePathCache(req);
            // 输出json格式的字符串回包
            Debug.WriteLine(AbstractModel.ToJsonString(resp));

            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            while (!cts.IsCancellationRequested) {
               var respQuery = await client.DescribePurgeTasks(new() {
                  TaskId = resp.TaskId
               });
               // 输出json格式的字符串回包
               Debug.WriteLine(AbstractModel.ToJsonString(respQuery));
               if (respQuery.PurgeLogs is { Length: > 0 }) {
                  var task = respQuery.PurgeLogs[0];
                  if (task.Status == "done") {
                     break;
                  }
               }
            }
         }
      } catch (Exception ex) {
         Debug.WriteLine(ex);
      }
   }

   private async Task PrefetchObjectKeysEdgeOne(IEnumerable<string> objectKeys) {
      try {
         if (!string.IsNullOrWhiteSpace(_config.cdnRefreshRoot)) {
            Credential cred = new Credential {
               SecretId = _config.secretId,
               SecretKey = _config.secretKey
            };
            // 实例化一个client选项，可选的，没有特殊需求可以跳过
            ClientProfile clientProfile = new ClientProfile();
            // 实例化一个http选项，可选的，没有特殊需求可以跳过
            // 实例化一个http选项，可选的，没有特殊需求可以跳过
            HttpProfile httpProfile = new HttpProfile {
               Endpoint = ("teo.tencentcloudapi.com")
            };
            clientProfile.HttpProfile = httpProfile;

            // 实例化要请求产品的client对象,clientProfile是可选的
            TeoClient client = new TeoClient(cred, _config.region, clientProfile);
            // 实例化一个请求对象,每个接口都会对应一个request对象
            CreatePrefetchTaskRequest req = new CreatePrefetchTaskRequest() {
               Targets = objectKeys.Select(a => Path.Combine(_config.cdnRefreshRoot, a).Replace(@"\", "/")).ToArray(),
               ZoneId = _config.edgeOneZoneId,
               EncodeUrl = true
            };

            if (req.Targets.Length == 0)
               return;

            // 返回的resp是一个CreatePurgeTaskResponse的实例，与请求对象对应
            var resp = await client.CreatePrefetchTask(req);
            // 输出json格式的字符串回包
            Debug.WriteLine(AbstractModel.ToJsonString(resp));

            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var reqQuery = new DescribePrefetchTasksRequest() {
               Filters = [new AdvancedFilter { Name = "job-id", Values = [resp.JobId] }]
            };
            while (!cts.IsCancellationRequested) {
               var respQuery = await client.DescribePrefetchTasks(reqQuery);
               // 输出json格式的字符串回包
               Debug.WriteLine(AbstractModel.ToJsonString(respQuery));
               if (respQuery.Tasks is { Length: > 0 }) {
                  var task = respQuery.Tasks[0];
                  if (task.Status == "success") {
                     break;
                  }
               }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token);
         }
      } catch (Exception ex) {
         Debug.WriteLine(ex);
      }
   }

   private async Task PurgeRootEdgeOne() {
      try {
         if (!string.IsNullOrWhiteSpace(_config.cdnRefreshPath)) {
            Credential cred = new Credential {
               SecretId = _config.secretId,
               SecretKey = _config.secretKey
            };
            // 实例化一个client选项，可选的，没有特殊需求可以跳过
            ClientProfile clientProfile = new ClientProfile();
            // 实例化一个http选项，可选的，没有特殊需求可以跳过
            HttpProfile httpProfile = new HttpProfile {
               Endpoint = ("teo.tencentcloudapi.com")
            };
            clientProfile.HttpProfile = httpProfile;

            // 实例化要请求产品的client对象,clientProfile是可选的
            TeoClient client = new TeoClient(cred, _config.region, clientProfile);
            // 实例化一个请求对象,每个接口都会对应一个request对象
            CreatePurgeTaskRequest req = new CreatePurgeTaskRequest() {
               Type = "purge_prefix",
               Method = "invalidate",
               Targets = new[] { _config.cdnRefreshPath },
               ZoneId = _config.edgeOneZoneId
            };

            // 返回的resp是一个CreatePurgeTaskResponse的实例，与请求对象对应
            CreatePurgeTaskResponse resp = await client.CreatePurgeTask(req);
            // 输出json格式的字符串回包
            Debug.WriteLine(AbstractModel.ToJsonString(resp));

            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var reqQuery = new DescribePurgeTasksRequest() {
               Filters = [new AdvancedFilter { Name = "job-id", Values = [resp.JobId] }]
            };
            while (!cts.IsCancellationRequested) {
               var respQuery = await client.DescribePurgeTasks(reqQuery);
               // 输出json格式的字符串回包
               Debug.WriteLine(AbstractModel.ToJsonString(respQuery));
               if (respQuery.Tasks is { Length: > 0 }) {
                  var task = respQuery.Tasks[0];
                  if (task.Status == "success") {
                     break;
                  }
               }

               await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token);
            }
         }
      } catch (Exception ex) {
         Debug.WriteLine(ex);
      }
   }

   public async Task CdnPrefetchObjectKeys(IEnumerable<string> objectKeys) {
      if (_config.useEdgeOne)
         await PrefetchObjectKeysEdgeOne(objectKeys);
      else
         await PrefetchObjectKeysCdn(objectKeys);
   }

   public async Task CdnPurgePath() {
      if (_config.useEdgeOne)
         await PurgeRootEdgeOne();
      else
         await PurgeRootCdn();
   }
}