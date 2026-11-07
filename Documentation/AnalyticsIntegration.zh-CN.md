# 统计与归因接入

先读 [SDK 用途与 Manager 清单](SdkGuide.zh-CN.md)。`com.novorizon.analytics` 提供行为统计和数数 / GameAnalytics 适配；`com.novorizon.attribution` 提供 AppsFlyer / Adjust 归因适配。Firebase 的统计桥接放在原 Firebase 包。外部 SDK 不随 all 自动下载，所有业务入口均可显式注入并持有。

## SDK 版本与可选程序集

| 实现 | 本次核对版本 | 程序集 | 手动符号 |
| --- | --- | --- | --- |
| 数数 ThinkingData | 3.5.5 | `Game.Framework.Analytics.ThinkingData` | `NOVORIZON_THINKINGDATA` |
| GameAnalytics | 8.3.1 | `Game.Framework.Analytics.GameAnalytics` | `NOVORIZON_GAMEANALYTICS` |
| AppsFlyer | 6.18.3 | `Game.Framework.Attribution.AppsFlyer` | `NOVORIZON_APPSFLYER_6` |
| Adjust | 5.8.2 | `Game.Framework.Attribution.Adjust` | `NOVORIZON_ADJUST_5` |
| Firebase Analytics | 14.1.0 | `Game.Framework.Firebase.Analytics` | 原 Firebase App / Analytics 符号 |

官方来源：[ThinkingData](https://github.com/ThinkingDataAnalytics/unity-sdk)、[GameAnalytics](https://github.com/GameAnalytics/GA-SDK-UNITY)、[AppsFlyer](https://github.com/AppsFlyerSDK/appsflyer-unity-plugin)、[Adjust](https://github.com/adjust/unity_sdk)。使用表内版本导入官方完整 SDK，并按厂商指南完成平台依赖解析、项目配置和构建设置。源码 / 二进制与工程凭据均留在消费工程。

UPM 安装的受支持版本会通过 versionDefines 启用对应适配程序集；使用 unitypackage 导入时，执行 **Novorizon > Analytics > Enable installed ...**。菜单设置的是当前构建目标组的符号，切换 Android / iOS 后分别配置。SDK 版本范围仅声明已知兼容的大版本，不代表未来版本均已实测。

AppsFlyer 7.x 要求更高 Unity 版本，本仓库 Unity 2022.3 基线选择 6.18.x，不会强行启用 7.x。已有 Firebase 产品仍使用其原来的按产品启用流程。

业务 asmdef 引用 `Game.Framework.Analytics` / `Game.Framework.Attribution` 及启动配置使用的可选适配程序集。Inspector 事件绑定另引用 `Game.Framework.Analytics.Unity`。

## 生命周期与采集开关

`AnalyticsManager` / `AttributionManager` 不在构造时初始化 SDK，`Start(collectionAllowed)` 才请求本地启动；传 false 时不启动，也不缓存业务事件。后来 `SetCollectionEnabled(true)` 会按当前用户启动；重新关闭时停止业务转发并调用 SDK 开关，关闭期间的业务埋点不会在以后重放。

所有 Manager 调用在已初始化的 Unity 主线程执行。Framework 默认安装线程调度；测试 / 自定义宿主应显式配置。一个 SDK 同时由一个本封装实例持有，不要同时运行厂商自动初始化组件和另一套业务初始化脚本。启动层在离开游戏会话时释放自有 Manager；默认 SDK App 等原生全局状态不被包装器删除。

SDK 自身的冷启动自动采集、已有本地缓存和自动 session 事件由厂商配置决定，必须在原生 SDK 初始化前按项目政策配置。C# 开关不能撤回此前上传数据。数数的明确关闭使用 `Stop` 清除 SDK 未上报缓存；正常包装器释放使用 `Pause` 保留已经接受的待发送数据。它们和业务层的“当前不再发送事件”是不同层次的状态。

## 数数：游客、登录与用户属性

```csharp
using Game.Framework.Analytics;
using Game.Framework.Analytics.ThinkingData;

var backend = new ThinkingDataAnalyticsBackend(appId, "https://your-receiver.example.com");
var analytics = new AnalyticsManager(new[] { backend }, ownsBackends: true);
analytics.SetUserId(gameUserId); // 可在 Start 前暂存；null 表示游客 / 退出账号
analytics.Start(collectionAllowed);
analytics.SetSuperProperties(new Dictionary<string, object>
{
    ["channel"] = "app_store",
    ["build"] = Application.version
});
analytics.LogEvent("level_start", new Dictionary<string, object> { ["level_id"] = "1_3" });
analytics.ApplyUserProperties(new UserPropertyChange(
    UserPropertyOperation.Set,
    new Dictionary<string, object> { ["level"] = 12, ["vip"] = true }));
analytics.ApplyUserProperties(new UserPropertyChange(
    UserPropertyOperation.Add,
    new Dictionary<string, object> { ["total_play_seconds"] = 90 }));
```

支持 Set / SetOnce / Add / Unset / Append / AppendUnique / DeleteProfile。Append 类操作传字符串列表；Unset 传需删除的 key（value 可为 null）。DeleteProfile 只删除统计侧用户资料，不删除游戏账号 / 存档，也不替代账号删除的服务端流程。

数数的 `distinct_id` 由 SDK 管理，`SetUserId` 对应 Login / Logout，不反复把设备标识换成游戏账号。需要排查游客关联时可以在启动后从 backend 读取 `GetDistinctId()`；不要将 token 当作用户 ID。可选 `autoTrackLifecycle:true` 启用 SDK 的安装 / 前后台事件，默认关闭自动事件。

公共属性只作用于本 Manager 的手动事件，单次事件的同名字段覆盖公共字段。属性和列表会复制，调用方之后的修改不影响本次提交。通用事件接受 string、bool、int/long、有限 float/double，不接收任意对象或嵌套字典。通用名称为 ASCII 字母开头，后接字母、数字、下划线，最多 64 字符；数数进一步限制名称 50 字符，并校验 SDK 的字符串 UTF-8 字节和数字范围。

## GameAnalytics：游戏专用事件

在厂商 Settings 中配置当前平台的 key / build、资源货币、item type 等，再在启动配置中改用 `GameAnalyticsBackend`。

普通 `AnalyticsManager.LogEvent` 映射为 Design Event。游戏用户映射到 SDK 可随时更新的 **external user ID**，保持原 SDK 用户标识；不使用初始化后难以更换的 custom ID 作为登录切换方案。

关卡、资源和付费报表使用该 adapter 的明确游戏事件：

```csharp
gameAnalytics.LogProgression(ProgressionStatus.Start, "chapter_1", "level_3");
gameAnalytics.LogProgression(ProgressionStatus.Complete, "chapter_1", "level_3", score: 1200);
gameAnalytics.LogResource(ResourceFlow.Sink, "gold", 100, "equipment", "sword_1");
gameAnalytics.LogBusiness("USD", 499, "iap", "gold_pack", "shop"); // SDK 要求整数 cents
```

这些是统计事件，不修改玩家背包。付费统计在业务验单发货成功之后触发，不把 SDK 统计当作验单 / 发货证据。GA 的资源名称、层级字段和 key 还需符合厂商规则并在控制台有相应配置。

## Firebase：复用同一个统计入口

`FirebaseManager.CreateAnalytics()` 返回可注入 `AnalyticsManager` 的 backend；原 `FirebaseAnalyticsAdapter` 和 `Game.Framework.Firebase.IAnalytics` 继续兼容。

Firebase 桥接支持普通事件、Set / Unset 用户属性、收入统计；SetOnce / Add / Append / DeleteProfile 不伪装成支持。桥接校验事件 / 参数名 40 字符、每事件 25 参数、字符串参数 100 字符，以及用户属性名 24 / 值 36 字符和保留前缀。更复杂 GA4 items 数组不在当前通用扁平事件 API 中。

## AppsFlyer / Adjust：买量归因

选择一个 MMP，保留独立的 `AttributionManager`，避免把每个点击事件默认当作归因转化。

```csharp
// AppsFlyer：逻辑事件名可以映射到厂商标准名。
var mmp = new AppsFlyerAttributionBackend(
    devKey,
    iosAppStoreId,
    new Dictionary<string, string> { ["purchase"] = "af_purchase" });
var attribution = new AttributionManager(mmp, ownsBackend: true);
attribution.AttributionChanged += OnAttributionChanged;
attribution.SetUserId(gameUserId);
attribution.Start(collectionAllowed);
```

```csharp
// Adjust：每个事件 token 由控制台生成，必须显式映射。
var mmp = new AdjustAttributionBackend(
    appToken,
    new Dictionary<string, string>
    {
        ["tutorial_complete"] = tutorialEventToken,
        ["purchase"] = purchaseEventToken
    },
    sandbox: true);
```

Adjust 上线配置显式改 `sandbox:false`。用户关联通过可更新的 `game_user_id` callback 参数完成，不把游戏账号当成 persistent external device ID，也不自动把该参数共享给广告合作伙伴。AppsFlyer 对应 customer user ID。调用 `SetUserId(null)` 清除当前账号关联。

```csharp
attribution.LogEvent("tutorial_complete");
attribution.LogRevenue(new RevenueEvent("purchase", 4.99m, "USD", transactionId));
```

收入使用明确金额、货币和交易 ID：AppsFlyer 映射 `af_revenue` / `af_currency` / `af_order_id`；Adjust 使用 SDK Revenue / Currency / DeduplicationId。统计 SDK 的订单字段不等于所有平台都保证持久化去重，业务应在已验证订单的统计流程中幂等处理，不反复把恢复购买计为新收入。未映射的 Adjust 事件明确返回 `Unsupported`。

归因回调转换成 Provider / Network / Campaign / Adgroup / Creative / IsOrganic 的快照，回到 Unity 主线程后再通知业务；不转发原始回调体和设备广告标识。调用方取消采集或释放后，排队中的回调不再通知。首版封装安装 / 打开来源回调，不实现 OneLink / deferred deeplink 页面路由、广告收入自动连接器或买量平台后台 API。

广告标识读取默认为关闭，可按工程同意状态与原生配置显式选择；这一个 bool 不替代厂商 CMP、ATT、控制台授权和原生隐私配置。

## 验证边界

本地 / CI 验证真实核心实现的分发、禁用、不重放、身份、不可变载荷、能力提示、错误隔离和释放；另用真实 SDK 程序集验证 API 与收入载荷，并在 Unity 验证 Inspector 事件及后台回调回主线程。

未使用真实 AppID / 项目凭据做线上采集或归因请求，未执行 Android APK/AAB 或 Xcode 原生构建及真机控制台入库验收。`AcceptedBySdk` 只代表本地调用完成；不会伪称服务端已经收到。准确版本、源码摘要及本轮结果见 [统计验证记录](AnalyticsValidation.json)。
