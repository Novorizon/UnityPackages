# Ads

独立 `AdManager` + `IAdProvider`；可选 AppLovin MAX 8.6.6 和字节**国内穿山甲**适配器，支持激励视频 / 全屏插屏。巨量引擎的广告投放 API、全球 Pangle 与国内穿山甲 SDK 不混用。

外部 SDK 不自动随包安装。MAX 使用官方 Integration Manager；穿山甲提供 iOS / Android 原生桥接及 EDM 依赖模板，执行 **Novorizon > Platform SDKs > Install domestic CSJ** 后 Resolve。首版接直连穿山甲，尚未接 GroMore 的其他广告网络、Banner、开屏或信息流。

关闭、播放完成都不直接发奖励；仅发布 SDK 的奖励校验事件。本地有界去重不代替服务端 SSV 和持久化幂等。取消展示等待后仍保留原生操作所有权。

安装和调用示例见 [平台服务接入](../../Documentation/PlatformServices.zh-CN.md)，独立包可查看[同版在线文档](https://github.com/Novorizon/UnityPackages/blob/v0.1.0-preview.37/Documentation/PlatformServices.zh-CN.md)。原生广告实际展示 / SSV 需项目 AppID、广告位、控制台与设备验收。

业务优先使用 `AdManager`；原 `AdsClient` API 继续兼容。全部 SDK 的分工见[用途清单](../../Documentation/SdkGuide.zh-CN.md)。
