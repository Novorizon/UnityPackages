# AttributionManager

面向买量归因的 AppsFlyer / Adjust 可选适配，独立于广告展示与行为统计。

只在启动配置中选择 SDK，业务使用 Manager；外部 SDK 不随 all 自动下载，也不在构造时初始化。

先读 [SDK 用途与 Manager 清单](../../Documentation/SdkGuide.zh-CN.md)，再按 [接入文档](../../Documentation/AnalyticsIntegration.zh-CN.md) 安装所选 SDK。独立导出包可查看[同版在线文档](https://github.com/Novorizon/UnityPackages/blob/v0.1.0-preview.37/Documentation/AnalyticsIntegration.zh-CN.md)。

本轮验证覆盖核心行为、真实 SDK API / 载荷和 Unity 回调；原生构建、真机及服务端入库仍需项目配置验收。
