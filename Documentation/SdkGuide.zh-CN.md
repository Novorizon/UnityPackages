# SDK 用途与业务入口

业务代码按功能调用 **Manager**，只在游戏启动配置里选择 SDK。不要在商城、战斗和 UI 脚本里分别直接调用各家 SDK。

## 业务只需记住这六个入口

| 业务入口 | 负责什么 | 业务调用示例 |
| --- | --- | --- |
| `IapManager` | 内购、商品价格、启动补单、恢复购买 | `await iap.PurchaseAsync("coins")` |
| `AdManager` | 加载 / 展示广告、接收奖励 | `await ads.ShowAsync(revivePlacement)` |
| `AnalyticsManager` | 玩家行为埋点、游戏用户关联、公共属性、用户属性 | `analytics.LogEvent("level_complete", properties)` |
| `AttributionManager` | 买量渠道归因、转化、收入统计 | `attribution.LogRevenue(revenue)` |
| `AuthManager` | 游客、第三方登录、绑定、会话 token | `await auth.LinkAsync(IdentityProvider.Apple)` |
| `FirebaseManager` | 初始化 Firebase，并按需创建具体 Firebase 产品 | `await firebase.InitializeAsync()` |

这些是显式创建、由启动层持有的 Manager 实例，不会因为读取一个全局属性就初始化外部 SDK。支付包自 `0.1.0-preview.3` 起统一使用 `IapManager`，原 `PaymentClient` 类型移除。`AdsClient`、`AuthClient`、`FirebaseRuntime` 保留兼容；新业务统一使用上表命名。名称不同不代表重复安装了一套 SDK。

## 各 SDK 到底干什么

| SDK / 平台 | 用途 | 什么时候需要 | 对应业务入口 |
| --- | --- | --- | --- |
| **Unity IAP** | 帮 Unity 对接不同商店的购买接口 | 当前 App Store / Google Play 内购适配所用的桥接库 | `IapManager` |
| **Apple App Store / Google Play Billing** | 商店内购买、取消、恢复、确认等 | 在相应商店销售数字商品 | `IapManager` |
| **穿山甲** | 在游戏里展示广告，开发者获得广告收入 | 国内游戏的激励视频 / 全屏插屏变现 | `AdManager` |
| **AppLovin MAX** | 广告聚合与展示变现 | 海外游戏广告变现；其他广告网络按 MAX 项目配置接入 | `AdManager` |
| **数数 ThinkingData** | 行为事件、留存、漏斗、付费分析、用户属性等 | 需要完整行为分析；国内、海外都可使用，按项目选服务区域 / 私有部署 | `AnalyticsManager` |
| **Firebase Analytics** | 通用事件、用户行为和转化分析 | 海外项目的基础统计，尤其已使用 Firebase 时 | `AnalyticsManager` |
| **GameAnalytics** | 游戏事件、关卡进度、资源产出 / 消耗、付费统计 | 需要面向游戏的关卡与经济分析 | `AnalyticsManager` |
| **AppsFlyer** | 将安装、转化和收入归属到投放渠道 / 广告活动 | 开始付费买量、核算渠道效果时 | `AttributionManager` |
| **Adjust** | 同样负责买量归因、转化和收入统计 | 与 AppsFlyer 按投放渠道支持和商务条件选择 | `AttributionManager` |
| **Firebase Crashlytics** | 崩溃、非致命异常与定位信息 | 需要知道玩家设备上的线上错误 | `FirebaseManager.CreateCrashReporter()` |
| **Firebase Auth** | 维护游戏用户身份、验证第三方凭据和账号绑定 | 使用 Firebase 作为账号后端 | `AuthManager`，启动时注入 `firebase.CreateAuthBackend()` |
| **Firebase Remote Config** | 远程下发活动开关、数值参数等 | 需要修改参数并由客户端拉取 / 激活 | `FirebaseManager.CreateRemoteConfig()` |
| **Firebase Cloud Messaging / FCM** | 设备推送消息 | 需要相应平台的推送能力；国内 Android 还要考虑设备和网络条件 | `FirebaseManager.CreateMessaging()` |
| **Apple / Google / Facebook 登录 SDK** | 打开各平台授权流程并获取身份凭据 | 游戏提供相应账号的登录按钮 | `AuthManager`，启动时配置身份 provider |
| **巨量引擎** | 开发者投放广告、买量的商业平台 | 需要购买玩家流量时配置投放与归因 | 投放后台及所选归因服务的渠道配置 |

容易混淆的两组：**穿山甲 / MAX 让游戏通过广告赚钱，AppsFlyer / Adjust 衡量买来的玩家来自哪里、带来多少转化。** AppLovin MAX 和 AppsFlyer 也分别属于这两类。

**Firebase 是产品集合**。Analytics、Crashlytics、Auth、Remote Config、FCM 分别负责统计、崩溃、账号、远程参数和推送，可以按功能安装。接了其中一个，不表示其他产品必须全部接入。

## 先选最小组合

- **国内项目**：行为统计先选数数；有广告变现时接穿山甲。内购按发行商店安排，当前已封装 App Store / Google Play，国内厂商支付需另加渠道适配。
- **海外项目**：基础统计先选 Firebase Analytics；崩溃接 Crashlytics；有广告变现时接 MAX。偏重游戏关卡 / 经济分析时可以改选 GameAnalytics；需要数数的分析能力也可继续选数数。
- **开始付费买量后**：再在 AppsFlyer、Adjust 中选一个 MMP，按实际投放渠道支持和商务条件决定。它们不承担游戏内广告展示。

同一款游戏可以按明确需求接多个统计目的地；本框架支持一次埋点分发，但默认不会替项目安装或同时初始化全部平台。先选一个主统计，建立稳定事件口径，再按需要扩展。

SDK 库与平台服务的收费不同。Firebase Analytics / Crashlytics 等基础产品的费用边界见 [平台服务说明](PlatformServices.zh-CN.md)；数数、MMP 和其他高级服务按相应平台方案核价。

## 业务代码长什么样

行为统计的调用不随 SDK 改动：

```csharp
analytics.SetUserId(gameUserId);
analytics.LogEvent("level_complete", new Dictionary<string, object>
{
    ["level_id"] = "chapter_1_3",
    ["score"] = 1200,
    ["duration_seconds"] = 92.5
});
```

只有启动配置选择具体实现：

```csharp
// 数数项目
var analytics = new AnalyticsManager(
    new IAnalyticsBackend[] { new ThinkingDataAnalyticsBackend(appId, receiverUrl) },
    ownsBackends: true);
analytics.SetUserId(gameUserId);
analytics.Start(collectionAllowed);
```

```csharp
// 已使用 Firebase 的海外项目
var firebase = new FirebaseManager();
await firebase.InitializeAsync();
var analytics = new AnalyticsManager(
    new IAnalyticsBackend[] { firebase.CreateAnalytics() },
    ownsBackends: true);
analytics.SetUserId(gameUserId);
analytics.Start(collectionAllowed);
```

UI Button / UnityEvent 可绑定 `AnalyticsEventTrigger.Track()`，在 Inspector 填事件名与参数。启动层调用 `trigger.Configure(analytics)` 注入同一个 Manager，不在 UI 中配置 SDK AppID 或创建统计服务。

`LogEvent` 返回每个目的地的本地提交结果。`AcceptedBySdk` 表示调用已交给 SDK；它不等于服务端已入库。不支持的操作会返回 `Unsupported`，SDK 调用异常会返回 `Failed`；可以据此排查配置。

详细 SDK 安装、命名空间、版本、用户属性与归因收入用法见 [统计与归因接入](AnalyticsIntegration.zh-CN.md)；支付、广告、账号与 Firebase 的配置见 [平台服务接入](PlatformServices.zh-CN.md)。
