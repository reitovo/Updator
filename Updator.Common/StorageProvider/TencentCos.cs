using System.Diagnostics;
using System.IO.Compression;
using COSXML;
using COSXML.Auth;
using COSXML.Model.Object;
using COSXML.Utils;
using Updator.Common;
using Updator.Common.CompressionProvider;

namespace Uploader.StorageProvider;

public class TencentCosConfig {
   public string region { get; set; }
   public string secretId { get; set; }
   public string secretKey { get; set; }
   public string bucket { get; set; }
   public string objectKeyPrefix { get; set; }
}

public class TencentCos : IStorageProvider {
   private TencentCosConfig _config;
   private CosXml _cosXml;
   private ICompressionProvider _compress = new Raw();

   public TencentCos(TencentCosConfig config) {
      _config = config;

      CosXmlConfig c = new CosXmlConfig.Builder()
         .SetRegion(config.region) // 设置默认的区域, COS 地域的简称请参照 https://cloud.tencent.com/document/product/436/6224 
         .Build();

      string secretId = config.secretId; // 云 API 密钥 SecretId, 获取 API 密钥请参照 https://console.cloud.tencent.com/cam/capi
      string
         secretKey = config.secretKey; // 云 API 密钥 SecretKey, 获取 API 密钥请参照 https://console.cloud.tencent.com/cam/capi
      long durationSecond = 600; //每次请求签名有效时长，单位为秒
      QCloudCredentialProvider qCloudCredentialProvider = new DefaultQCloudCredentialProvider(secretId,
         secretKey, durationSecond);

      Crc64.InitECMA();
      _cosXml = new CosXmlServer(c, qCloudCredentialProvider);
   }

   public void SetCompression(ICompressionProvider compression) {
      _compress = compression;
   }

   public async Task UploadAsync(string objectKey, Stream fileStream, Action<(long Done, long Total)> progress = null) {
      try {
         using var ms = new MemoryStream();
         await _compress.Compress(fileStream, ms);

         string bucket = _config.bucket; //存储桶，格式：BucketName-APPID
         string key = $"{_config.objectKeyPrefix}{objectKey}"; //对象键 
         PutObjectRequest request = new PutObjectRequest(bucket, key, ms);
         //设置进度回调
         request.SetCosProgressCallback(delegate(long completed, long total) {
            Debug.WriteLine($"progress = {completed * 100.0 / total:##.##}%");
            progress?.Invoke((completed, total));
         });
         //执行请求
         PutObjectResult result = _cosXml.PutObject(request);
         //打印请求结果
         Debug.WriteLine(result.GetResultInfo());
      } catch (COSXML.CosException.CosClientException clientEx) {
         //请求失败
         Debug.WriteLine("CosClientException: " + clientEx);
      } catch (COSXML.CosException.CosServerException serverEx) {
         //请求失败
         Debug.WriteLine("CosServerException: " + serverEx.GetInfo());
      }
   }

   public async Task DownloadAsync(string objectKey, Stream fileStream,
      Action<(long Done, long Total)> progress = null) {
      try {
         // 存储桶名称，此处填入格式必须为 bucketname-APPID, 其中 APPID 获取参考 https://console.cloud.tencent.com/developer
         string bucket = _config.bucket;
         string key = $"{_config.objectKeyPrefix}{objectKey}"; //对象键 
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

         await _compress.Decompress(new MemoryStream(result.content), fileStream);
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

   public async Task<bool> CheckSameAsync(string objectKey, string checksum) {
      return await Task.Run(() => {
         try {
            string bucket = _config.bucket; //存储桶，格式：BucketName-APPID
            string key = $"{_config.objectKeyPrefix}{objectKey}"; //对象键 
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

   public async Task<string> CalculateChecksum(Stream fileStream) {
      using var ms = new MemoryStream();
      await _compress.Compress(fileStream, ms);

      ulong crc1 = 0;
      int num;
      byte[] numArray = new byte[2048];
      while ((num = ms.Read(numArray, 0, numArray.Length)) > 0) {
         ulong crc2 = Crc64.Compute(numArray, 0, num);
         crc1 = crc1 != 0UL ? Crc64.Combine(crc1, crc2, (long) num) : crc2;
      }
      return crc1.ToString();
   }
}