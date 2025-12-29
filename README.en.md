# Updator
Very simple but straight forward application updater!

The project contains:
- `uploader` for uploading releases to cloud, currently supports `tencent-cloud-cos`, `azure-blobs`, and `s3-compatible` storage, with CDN refresh support, but it is easy to support more.
- `downloader` for downloading releases, currently it use http(s) to download from links.
- `publisher` for publish downloader itself. Could be helpful if you need to publish your own downloader.

All of them are CLI applications.  
![Screenshot 2023-01-11 140830](https://user-images.githubusercontent.com/29846655/211730508-fb8ac360-2de6-401e-a5ff-489608ef8663.png)

## Uploader
The program reads `config.json` to upload specified folder to cloud.

An example:
```json
{
  "projectName": "弹幕姬",
  "distributionRoot": "D:/Work/BililiveAssist/Output",
  "versionString": "1.3.3",
  "buildId": 13300,
  "autoIncreaseBuildId": true,
  "channel": "release",
  "executable": "弹幕姬.exe",
  "ignored": [
    "弹幕姬_BurstDebugInformation_DoNotShip/" 
  ],
  "compression": "brotli",
  "storage": "cos",
  "cos": {
    "region": "ap-nanjing",
    "secretId": "YOUR-SECRET-ID",
    "secretKey": "YOUR-SECRET-KEY",
    "bucket": "dist-1304010062",
    "objectKeyPrefix": "bliveassist/release/",
    "cdnRefreshRoot": "https://dist.reito.fun/bliveassist/release"
  },
  "updateLogs": [
    {
      "buildId": 13300,
      "versionString": "1.3.3",
      "items": {
        "_": [
          "1. 新增：弹幕(列表)可以不折叠重复弹幕",
          "2. 新增：弹幕(列表)与弹幕(滚动)可以屏蔽所有表情弹幕",
          "3. 新增：主播可以自定义自己房间在弹幕姬中的一些元素，如用Logo替代历史记录中的文字",
          "4. 修复：布局保存问题",
          "5. 修复：全面屏适配"
        ]
      }
    }, 
    {
      "buildId": 13100,
      "versionString": "1.3.1",
      "items": {
        "_": [
          "1. 修复：部分房间无法使用",
          "2. 修复：布局无法保存",
          "3. 新增：主播可以自定义自己房间在弹幕姬中的一些元素，如用Logo替代历史记录中的文字",
          "4. 修复：布局保存问题",
          "5. 修复：全面屏适配"
        ]
      }
    } 
  ]
}
```

One can extend the ability of this app by adding providers to `StorageProvider`, `CompressionProvider`, `ChecksumProvider`.

Currently supported providers:
- `cos`: Tencent cloud COS, also support refreshing CDN if you use such service.
- `azure-blobs`: Azure Storage Blobs, support Front Door Endpoint refresh.
- `s3`: S3 Compatible (supports AWS S3, MinIO, Wasabi, and all S3-compatible storage services)

### S3 Compatible Configuration Examples

AWS S3:
```json
{
  "storage": "s3",
  "checksum": "s3-md5",
  "s3": {
    "endpoint": "https://s3.amazonaws.com",
    "region": "us-east-1",
    "accessKeyId": "YOUR-ACCESS-KEY-ID",
    "secretAccessKey": "YOUR-SECRET-ACCESS-KEY",
    "bucket": "your-bucket-name",
    "objectKeyPrefix": "myapp/release/",
    "forcePathStyle": false,
    "useHttp": false
  }
}
```

MinIO (or other S3-compatible services):
```json
{
  "storage": "s3",
  "checksum": "s3-md5",
  "s3": {
    "endpoint": "http://localhost:9000",
    "region": "us-east-1",
    "accessKeyId": "minioadmin",
    "secretAccessKey": "minioadmin",
    "bucket": "your-bucket-name",
    "objectKeyPrefix": "myapp/release/",
    "forcePathStyle": true,
    "useHttp": true
  }
}
```

Notes:
- S3 uses `s3-md5` as the checksum method (based on S3 ETag)
- `forcePathStyle`: Set to `true` for MinIO and similar services, `false` for AWS S3
- `useHttp`: Only set to `true` for local testing, use HTTPS in production

## Downloader
It reads `sources.json` to download from specified url. Then downloads the `<DISTRIBUTION-URL>/__description.json` to get all metadata about the distribution, compare and download all files needed.

It can also self-update `sources.json` by specifying `sourcesUrl`.

It can for sure update downloader itself, by default, it reads from github release, but you can also specify `customDownloaderUrl` to build your own downloader or speed up the upgrade.

An example:
```json
{
    "version": 4, 
    "sourcesUrl": "https://dist.reito.fun/bliveassist/sources.json",
    "customDownloaderUrl": "https://dist.reito.fun/downloader",
    "sources": [
        {
            "enable": true,
            "distributionUrl": "https://dist.reito.fun/bliveassist/release"
        }
    ]
}
```
## Publisher
It contains a publish helper to publish the downloader to platforms, and upload to tencent-cos and github. It reads tokens from config.json by passing the file's path as arg. The program could be useful if you want to publish your own downloader.
