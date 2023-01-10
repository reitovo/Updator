# Updator
Very simple but straight forward application updator

The project contains:
- an uploader for uploading releases to cloud, currently only `tencent-cloud-cos` is supported, but it is easy to support more.
- A downloader for downloading releases, currently it use http(s) to download from links.

Both of them are CLI applications.

## Uploader
One can extend the ability of this app by adding providers to `StorageProvider`, `CompressionProvider`, `ChecksumProvider`.

The program reads `config.json` to upload specified folder to cloud.

An example:
```json
{
  "projectName": "VTSLink",
  "distributionRoot": "C:/Users/reito/Desktop/VTSLink/Client/VTSLink/out/install/x64-Debug",
  "versionString": "0.9.0",
  "buildId": 100,
  "autoIncreaseBuildId": true,
  "channel": "debug",
  "executable": "bin/VTSLink.exe",
  "ignored": [
    "crash/",
    "bin/.vs",
    "vtslink.log"
  ],
  "compression": "brotli",
  "storage": "cos",
  "cos": {
    "region": "ap-nanjing",
    "secretId": "<YOUR-SECRET-ID>",
    "secretKey": "<YOUR-SECRET-KEY",
    "bucket": "dist-1304010062",
    "objectKeyPrefix": "vtslink/debug/"
  }
}
```
For more please read the code.

## Downloader
It reads `sources.json` to download from specified url. It first reads the `<DISTRIBUTION-URL>/__description.json` to get all metadata about the distribution, then compare and download all files needed.

It can also update `sources.json` by specifying `sourcesUrl`.

It can update itself, by default, it reads from github release, but you can also specify `customDownloaderUrl` to build your own downloader or speed up.

An example:
```json
{
    "version": 2,
    "sourcesUrl": "https://dist-1304010062.cos.ap-nanjing.myqcloud.com/vtslink/sources.json",
    "customDownloaderUrl": "https://github.com/cnSchwarzer/Updator/releases/latest/download",
    "sources": [
        {
            "enable": true,
            "distributionUrl": "https://dist-1304010062.cos.ap-nanjing.myqcloud.com/vtslink/debug"
        }
    ]
}
```

Moreover, to publish, please use
```
dotnet publish -r win-x64 -c Release --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=true
```
