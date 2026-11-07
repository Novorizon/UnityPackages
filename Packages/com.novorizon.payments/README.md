# Payments

Apple App Store / Google Play Billing 数字商品内购，基于 Unity IAP 5.5.x。核心 `PaymentClient` 不依赖原生 SDK；`UnityIapStore` 在安装兼容 IAP 版本后启用。

支持商品查询、购买、取消 / Deferred、恢复与补单、确认。只有注入的 `IPurchaseFulfillment` 在可信服务端完成验单和持久化发货后才确认；没有默认“验单通过”的实现。恢复的非消耗品 / 订阅重新校验权益，已完成消耗品不恢复发货。订阅续期 / 退款 / 撤销以服务端通知为准。

这不是 Apple Pay / Google Pay 钱包封装。详细安装和业务示例见 [平台服务接入](../../Documentation/PlatformServices.zh-CN.md)，独立包可查看[同版在线文档](https://github.com/Novorizon/UnityPackages/blob/v0.1.0-preview.36/Documentation/PlatformServices.zh-CN.md)。沙盒商店与服务端闭环需项目真机验收。
