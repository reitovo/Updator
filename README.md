# Updator
简单易用的应用更新器。帮助你解决分发应用的最后一步，只需简单配置即可上传应用至云服务，支持压缩，并且在客户端仅更新差异文件以节约流量。

[English](README.en.md)

本项目包含:
- `uploader` 用来上传你的应用至一个云端，目前支持`腾讯云COS`且支持CDN刷新，采用接口设计，也可以很快地为其他云端实现上传机制。
- `downloader` 用来提供给用户下载你的应用，目前使用纯HTTP(S)下载，因为云服务对象储存通常都是这种方式
- `publisher` 用来分发 `downloader`，因为其支持自我更新。如果你想开发自己的 `downloader` 其可以协助你进行分发流水线。

以上全是控制台应用，使用了 [Spectre.Console](https://spectreconsole.net/) 进行美化，使用 .NET 7 进行开发

![QQ截图20230111140207](https://user-images.githubusercontent.com/29846655/211731428-c8034a7a-d7fc-46ce-8a18-1ac3b09b69a6.png)

## Uploader 
程序通过解析 `config.json` 来上传至一个云服务

配置文件样例：
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

通过开发新的 `StorageProvider`, `CompressionProvider`, `ChecksumProvider`，可以很快的集成其他云服务到本项目，欢迎PR！ 

当前支持的云服务：
- `cos`: 腾讯云COS，支持CDN刷新

## Downloader 
程序读取 `sources.json` 去下载你的应用。

配置文件样例：
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

通过配置 `distributionUrl`，程序下载`<DISTRIBUTION-URL>/__description.json`来获取上传的应用元数据，并且对现有文件进行比较，对差异文件进行下载更新

通过配置 `sourcesUrl`，程序可以对 `sources.json` 进行自动更新

通过配置 `customDownloaderUrl`，程序可以访问该地址进行启动器的自我更新，默认是通过本github仓库的release进行更新

## Publisher
这是一个流水线应用，用来把 `downloader` 发布到github以及腾讯云COS上，如果你需要分发自己的 `downloader` 他或许可以帮你自动化完成一些操作
