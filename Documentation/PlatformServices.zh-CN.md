# 移动平台服务接入

本次新增 Auth、Payments、Ads、Firebase 四个独立包。`all` 安装这些自有包，不下载广告、支付、社交或 Firebase 的原生 SDK，不初始化它们，不包含 AppID、广告位、商户凭据或 Firebase 项目配置。游戏启动层明确创建所需实例并负责生命周期；不增加全局 Manager。

## 平台与 SDK 选择

| 业务 | 实现 | 本次核对版本 |
| --- | --- | --- |
| iOS / Google Play 数字商品内购 | Unity IAP，Apple App Store / Google Play Billing | Unity IAP 5.5.0 |
| 海外应用内广告 | AppLovin MAX | Unity 8.6.6，原生 13.6.4 |
| 国内应用内广告 | 字节穿山甲国内版，激励视频 / 全屏插屏 | Android `com.pangle.cn:mediation-sdk:7.8.1.4`，iOS `Ads-CN/BUAdSDK:7.8.1.1` |
| 分析、崩溃、远程参数、推送、账号后台 | 按产品导入官方 Firebase Unity SDK | 14.1.0，各产品必须同版 |
| Apple 身份 | iOS AuthenticationServices | iOS 13+ 原生 API |
| Google 身份 | Android Credential Manager + Google ID；iOS GoogleSignIn | Credentials 1.5.0 / Google ID 1.1.1；GoogleSignIn 10.1.0 |
| Facebook 身份 | 官方 Facebook Unity SDK，iOS Limited Login | 19.0.0 |

**巨量引擎主要用于广告投放、买量；穿山甲用于在游戏内展示广告并变现。** 它们属于成熟的字节商业化体系。国内还常用腾讯优量汇、快手；选型应比较填充率、eCPM、留存和实际收入，不能仅凭平台名称判断收益。第一版接直连穿山甲库存，`useMediation=false`；尚未封装 GroMore 多广告网络聚合、开屏、Banner 或信息流。AppLovin 和穿山甲分别创建 provider，不同时争用全屏展示。

这是商店内购，不是 Apple Pay / Google Pay 钱包。Steam 和国内手机厂商支付不在本次适配范围。

## 安装步骤

1. 从 Package Manager 安装所需自有包及其依赖；`all` 已提供完整自有包集合。外部 SDK 使用各厂商官方来源。
2. 内购：安装 `com.unity.purchasing` **5.5.x**，满足版本范围后自动编译 `Game.Framework.Payments.UnityIap`。IAP 4.x、5.0～5.4、6.x 不自动启用本适配器。
3. 原生 Apple / Google 登录：先安装 [External Dependency Manager for Unity](https://github.com/googlesamples/unity-jar-resolver)，选择构建目标，执行 **Novorizon > Platform SDKs > Install native identity**，再运行 Android Resolver / iOS Resolver。模板复制到 `Assets/NovorizonPlatform/Auth`，已有文件不覆盖。Google 原生桥接的组合要求 **Android API 23+ / iOS 15+**。iOS 创建唯一的 **Create > Novorizon > Native Identity Settings** 资产，填写公开的 Google iOS OAuth client ID；构建处理添加反向 URL Scheme 和 Apple 登录 entitlement。Apple 开发者后台仍需启用 Sign in with Apple，签名描述文件需相应能力。Google Android 后台需配置包名和签名 SHA；使用 Web OAuth client ID 获取 ID token。
4. 穿山甲：安装 EDM，执行 **Install domestic CSJ** 并 Resolve。模板复制到 `Assets/NovorizonPlatform/Ads`，通过 Maven / CocoaPods 解析原生库。SDK AppID 与广告位 ID 在穿山甲控制台申请；先用平台测试流量验收。iOS 还需按官方 SDK 文档补充所需 SKAdNetwork、隐私清单和项目设置。广告 SDK 在业务确认隐私同意后才初始化。
5. AppLovin：导入官方 MAX Unity SDK，用其 Integration Manager 配置 SDK Key 和网络依赖，再执行 **Enable installed AppLovin MAX**。本包不使用已弃用的 `SetSdkKey`，不自动配置其他广告网络。
6. Facebook：导入官方 SDK 并完成 App ID / Client Token / Bundle ID / URL Scheme 设置，再执行 **Enable installed Facebook SDK**。不要把 Facebook App Secret 放进客户端。iOS 默认 Limited Login，不假设用户已授予 ATT。
7. Firebase：只导入需要的 `FirebaseAnalytics.unitypackage`、`FirebaseCrashlytics.unitypackage`、`FirebaseRemoteConfig.unitypackage`、`FirebaseMessaging.unitypackage`、`FirebaseAuth.unitypackage`，放置项目自己的 `google-services.json` / `GoogleService-Info.plist`，Resolve 后执行 **Synchronize installed Firebase SDKs**。

菜单设置的是**当前构建目标组**的符号。切换 iOS / Android 时分别执行；移除 SDK 时删除相应符号并同步 Firebase。Firebase/FB 菜单识别标准 `Assets` 下的官方 DLL 安装；自定义 UPM 分发可按下表手动设置。游戏业务 asmdef 也须显式引用使用的适配程序集。

| 编译符号 | 适配程序集 |
| --- | --- |
| `NOVORIZON_NATIVE_AUTH` | `Game.Framework.Auth.Native` 内的原生桥接调用 |
| `NOVORIZON_FACEBOOK` | `Game.Framework.Auth.Facebook` |
| `NOVORIZON_MAX` | `Game.Framework.Ads.AppLovin` |
| `NOVORIZON_CSJ` | `Game.Framework.Ads.Csj` |
| `NOVORIZON_FIREBASE_APP` | `Game.Framework.Firebase.App` |
| `NOVORIZON_FIREBASE_AUTH` | `Game.Framework.Firebase.Auth` |
| `NOVORIZON_FIREBASE_ANALYTICS` | `Game.Framework.Firebase.Analytics` |
| `NOVORIZON_FIREBASE_CRASHLYTICS` | `Game.Framework.Firebase.Crashlytics` |
| `NOVORIZON_FIREBASE_REMOTE_CONFIG` | `Game.Framework.Firebase.RemoteConfig` |
| `NOVORIZON_FIREBASE_MESSAGING` | `Game.Framework.Firebase.Messaging` |

Native~ 是待安装的项目模板，不会自动进入构建；修改过的模板升级时需人工比较差异。Android 构建处理将桥接 keep rules 写入 Unity library 的 consumer rules，以支持启用混淆。账号凭据和广告回调接收器有 IL2CPP 保留声明。

## 业务如何调用

在 Unity 主线程、Framework 的线程调度已初始化后创建实例。普通场景下 Framework 自动初始化；自定义宿主可明确 `UnityThread.Initialize()`。不要从线程池直接操作客户端。

登录和绑定是不同的操作，绑定不会按邮箱自动合并两份存档：

```csharp
var firebase = new FirebaseRuntime();
await firebase.InitializeAsync();
var auth = new AuthClient(
    new FirebaseAuthBackend(firebase),
    new IIdentityProvider[]
    {
        new NativeIdentityProvider(IdentityProvider.Apple),
        new NativeIdentityProvider(IdentityProvider.Google, googleWebClientId, googleIosClientId),
        new FacebookIdentityProvider()
    },
    ownsDependencies: true);
if (auth.CurrentUser == null)
{
    await auth.SignInAnonymouslyAsync();
}
AuthUser user = await auth.LinkAsync(IdentityProvider.Google); // 原来的游客 UID
string sessionToken = await auth.GetSessionTokenAsync();      // 发给游戏服务端验证，不写日志
```

以上类型分别位于 `Game.Framework.Auth`、`.Auth.Native`、`.Auth.Facebook`、`.Firebase.Unity`。Apple 当前仅 iOS；Google 支持 Android / iOS；Facebook 使用其官方 SDK。Apple 在其他平台的 Web 登录未实现。绑定凭据已属于其他账号时返回 SDK 错误，业务展示账号恢复入口；不得自动改绑或只按同一邮箱合并。解除最后一种可用登录方式会被拒绝。注销、删除账号、Apple token 撤销与存档合并需要服务端业务处理，本包不提供假实现。

内购只需配置商品与项目自己的服务端发货实现：

```csharp
var store = new UnityIapStore(
    NativeStore.GooglePlay, // iOS 使用 Apple
    new[] { new StoreProduct("coins", "com.example.game.coins", StoreProductKind.Consumable) });
var payments = new PaymentClient(store, verifiedGameBackend, ownsStore: true);
await payments.InitializeAsync();
await payments.RecoverAsync(); // 启动时查已知权益、补处理未确认订单
PaymentResult result = await payments.PurchaseAsync("coins");
```

类型位于 `Game.Framework.Payments` / `.Payments.UnityIap`。`verifiedGameBackend` 实现 `IPurchaseFulfillment.VerifyAndGrantAsync`，只在**可信服务端验单并完成持久化发货**后返回成功，再由客户端确认 / 消耗订单。不要提供“总是成功”的客户端验单器。

业务的“恢复购买”按钮调用 `RestoreAsync()`；Apple 会进入明确的 RestoreTransactions 流程。启动补单使用 `RecoverAsync()`，不会主动调用这个恢复 UI。

- 服务端验证商店签名、应用 / 商品 / 环境、购买状态和归属，从已验证的收据中读取订单标识。Google 按 purchase token、Apple 按 transaction ID 等官方语义幂等；不要信任客户端传入的 ID 或仅依赖本地去重。用户归属由当前服务端会话验证，不能只信任请求里的 UID。
- 网络失败、取消或发货失败不会确认订单；下次启动 / 恢复继续补单。重复验单必须返回原来的发货结果。已确认的非消耗品 / 订阅可重新校验权益，不再次确认；已完成消耗品不作为库存恢复。
- Ask to Buy 等延迟订单返回 Deferred，不发货。订阅续期、退款、撤销、过期和跨设备状态以服务端商店通知及查单为准；本包没有把本地历史列表当作永久订阅权益。
- Unity IAP 服务实例由 SDK 共享，不要再并行使用 Codeless IAP 或另一个脚本确认同一批订单。生产环境明确选择原生商店；Editor Fake Store 只能辅助交互，不代表真实购买通过。

广告只选一个 provider，加载成功后展示；关广告不会直接加钱：

```csharp
var ads = new AdsClient(new CsjAdProvider(csjAppId, privacyConsent: true, rewardUserId: user.Id), ownsProvider: true);
// 海外可换成 new MaxAdProvider(hasUserConsent, user.Id)
ads.RewardEarned += OnRewardEarned;
await ads.InitializeAsync();
var placement = new AdPlacement("revive", rewardedUnitId, AdFormat.Rewarded);
await ads.LoadAsync(placement);
await ads.ShowAsync(placement);
```

类型位于 `Game.Framework.Ads` / `.Ads.Csj` / `.Ads.AppLovin`。`RewardEarned` 与关闭结果独立；穿山甲仅在基础奖励校验成功回调时发事件，支持关闭后迟到的奖励，保留最近 256 个展示身份，原生广告对象关闭后保留两分钟。MAX 使用官方 `OnAdReceivedRewardEvent` 的生命周期回调；不把播放完成或关闭当作奖励。客户端最近 1024 个奖励键去重仅防同会话重复回调，有经济价值的奖励由服务端 SSV 校验并持久化幂等；不要将可伪造的客户端事件当成服务端验奖。首版不处理穿山甲进阶奖励。

MAX 的回调没有返回 customData，因此本适配器将 SDK 的 placement 字段设为本次展示 GUID，保留最近 256 次展示以接收迟到奖励；业务稳定场景 ID 始终由 `AdReward.PlacementId` 返回。MAX 控制台报表建议按 ad unit 或游戏自己的 Analytics 场景聚合；不要依赖 SDK placement 字段进行稳定场景命名或频控。SSV 配置需把游戏用户 ID 和 customData（ShowId）关联到服务端奖励，示例通过 provider 构造参数设置用户 ID。

`auth` / `payments` / `ads` 的取消表示停止当前调用的等待，不能撤销支付、关掉所有平台弹窗或回滚已经完成的登录。`IsBusy` 会保留到原生操作真正结束，期间拒绝第二次调用。Dispose 会停止业务通知并延后释放正在操作的自有 provider。项目应在启动层持有这些客户端；SDK 永久不回调时仍保持占用，不用超时伪装弹窗已经关闭。重试、离线提示由业务决定。

## Firebase 与费用

Crashlytics 是 **Firebase 产品之一**，有独立 Unity 导入包；不是和 Firebase 毫无关系的第二套平台。只使用 Crashlytics 时无需同时引入数据库、推送或 Auth。Analytics 可选，便于崩溃前事件面包屑。

目前官方将 Analytics、Crashlytics、Remote Config、Cloud Messaging 列为免费产品；Firebase 整体有 Spark / Blaze 方案，短信认证、Identity Platform 额度、数据库 / 存储 / 云函数及 BigQuery 等服务需分别核价。参见 [Firebase 官方价格表](https://firebase.google.com/pricing)，不要把“Firebase 免费”理解成所有产品和用量无上限。

`FirebaseAnalyticsAdapter`、`FirebaseCrashReporter`、`FirebaseRemoteConfiguration`、`FirebasePushMessaging` 按需创建，注入已初始化的同一个 `FirebaseRuntime`。没有自动把 Unity 的所有日志上传；Crashlytics 官方 SDK 的崩溃收集与本包的显式非致命异常上报共存。代码不要把身份 token、支付收据或个人信息当作分析参数 / Crashlytics key。

采集是否在冷启动时允许，必须在**原生 SDK 初始化之前**通过项目配置决定。之后调用 `SetCollectionEnabled` 不能撤回此前采集。按项目政策配置 Android manifest / iOS Info.plist 的 Analytics、Crashlytics、Messaging auto-init 设置，再由业务显式开启需要的产品。SDK 默认 App / Auth / RemoteConfig 实例属于工程，包装器 Dispose 不删除它们，也不替其他调用者登出。

### Firebase 14.1 推送注册

本包使用新的 `InstallationRegistered` / `RegisterAsync`，向后端提交 **Firebase Installation ID**，不是登录凭据。按 [官方 Unity FCM 接入文档](https://firebase.google.com/docs/cloud-messaging/unity/get-started) 在 Android `<application>` 中加入：

```xml
<meta-data android:name="firebase_messaging_installation_id_enabled" android:value="true" />
```

iOS Info.plist 设置 `FirebaseMessagingInstallationIdEnabled = YES`，配置 APNs Key / 能力 / 签名；国内 Android 上 FCM 还依赖设备和网络条件，不能替代小米 / OPPO 等厂商推送。

```csharp
var push = new FirebasePushMessaging(firebase);
push.InstallationRegistered += BindInstallationToGameAccount;
push.MessageReceived += ShowInGameMessage;
await push.RequestPermissionAsync(cancellationToken); // Android 13+ 会请求通知权限
await push.RegisterAsync(cancellationToken);
```

先订阅业务事件再 Register；Dispose 只解除本实例事件，不全局注销其他消费者。需要明确退出推送时调用 `UnregisterAsync`，并在业务后端撤销设备与游戏账号的关联。

## 验收范围

基础行为测试进入仓库 CI；原生 Unity 验收包含 SDK 缺席时的全包编译及回调生命周期。另用下载自官方的真实 IAP / MAX / Facebook / Firebase SDK 核对 C# 接口与 Unity 程序集，并用真实 Android SDK 类验证 Java 桥接编译。

**Linux Editor 编译、测试 provider 和 SDK 接口编译不代表原生设备闭环通过。** 当前没有完成 Xcode 编译 / 真机签名、Apple / Google 沙盒购买、线上广告填充与 SSV、第三方登录回调或 Firebase 项目收发验收；它们需要项目自己的 AppID、控制台、签名及设备。版本、实际已运行项目和源文件摘要见 [SDK 验证记录](PlatformSdkValidation.json)，不要将首版预览包当作已经上线验收的服务端系统。
