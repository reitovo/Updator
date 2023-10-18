# Updator
简单易用的应用更新器。

帮我自己解决分发应用的最后一步，只需简单配置即可上传应用至云服务，支持压缩，在客户端仅更新差异文件以节约流量。

[English](README.en.md)

本项目包含:
- `Updator.Uploader` 用来上传你的应用至一个云端，目前支持`腾讯云COS`且支持CDN刷新，采用接口设计，也可以很快地为其他云端实现上传机制。
- `Updator.Downloader.CLI` 用简易控制台界面，来提供给用户下载你的应用，目前使用纯HTTP(S)下载，因为云服务对象储存通常都是这种方式
- `Updator.Downloader.UI` 使用 AvaloniaUI 制作了一个简易的界面，提高美观性

此外：
- `Updator.Downloader.Publish` 用来分发 `Updator.Downloader.*`，因为启动器支持自我更新。

使用 .NET 7 进行开发

`Updator.Downloader.CLI` 使用了 [Spectre.Console](https://spectreconsole.net/) 进行美化，

![QQ截图20230111140207](https://user-images.githubusercontent.com/29846655/211731428-c8034a7a-d7fc-46ce-8a18-1ac3b09b69a6.png)

`Updator.Downloader.UI` 使用了 [AvaloniaUI](https://avaloniaui.net/)

![image](https://github.com/cnSchwarzer/Updator/assets/29846655/09d3e25f-e8e5-4d6c-a06c-b2cf43cc6d7f)

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
  "reinstallBuildId": [
    13300
  ],
  "updateLogUrl": "https://www.wolai.com/reito/pfX88oqN1Wquk4BYmpubN",
  "appIconUrl": "https://dist.reito.fun/songchan/icon.png",
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

通过开发新的 `StorageProvider`, `CompressionProvider`, `ChecksumProvider`，可以很快的集成其他云服务到本项目 

当前支持的云服务：
- `cos`: 腾讯云COS，支持CDN刷新

## Downloader 
程序读取 `sources.json` 去下载你的应用。

配置文件样例：
```json
{
  "version": 3,
  "sourcesUrl": "https://direct.dist.reito.fun/songchan/win/sources.json",
  "customDownloaderUrl": "https://direct.dist.reito.fun/downloader",
  "defaultSourceId": "action",
  "defaultName": "点歌姬",
  "defaultIcon": "https://dist.reito.fun/songchan/icon.png",
  "sources": [
    {
      "enable": true,
      "id": "action",
      "distributionUrl": "https://direct.dist.reito.fun/songchan/win/action"
    }
  ]
}

```

通过配置 `distributionUrl`，程序下载`<DISTRIBUTION-URL>/__description.json`来获取上传的应用元数据，并且对现有文件进行比较，对差异文件进行下载更新

通过配置 `sourcesUrl`，程序可以对 `sources.json` 进行自动更新

通过配置 `customDownloaderUrl`，程序可以访问该地址进行启动器的自我更新，默认是通过本github仓库的release进行更新

## Publisher
这是一个流水线应用，用来把 `downloader` 发布到github以及腾讯云COS上，如果你需要分发自己的 `downloader` 或许可以帮你自动化完成一些操作
