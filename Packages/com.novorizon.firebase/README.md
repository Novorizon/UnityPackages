# Firebase

统一的分析、崩溃、远程参数与推送契约，按需启用官方 Firebase Unity 14.1.0 的 Analytics / Crashlytics / Remote Config / Messaging / Auth。`FirebaseAuthBackend` 实现 Auth 包的后端，不把第三方登录 SDK 和 Firebase Auth 混为一件事。

导入所需官方 unitypackage，配置工程自己的 Firebase 项目文件，Resolve 后执行 **Novorizon > Platform SDKs > Synchronize installed Firebase SDKs**。每个产品单独程序集；只用 Crashlytics 不要求导入其他产品。不会代替工程设置冷启动采集同意。

Messaging 使用 14.1 的 Installation ID 注册接口，支持 Android 13+ 通知权限；需要原生标志、iOS APNs 配置。推送地址和账号登录凭据不同。

完整配置、费用边界、符号表与业务示例见 [平台服务接入](../../Documentation/PlatformServices.zh-CN.md)，独立包可查看[同版在线文档](https://github.com/Novorizon/UnityPackages/blob/v0.1.0-preview.37/Documentation/PlatformServices.zh-CN.md)。实际云端收发 / 崩溃上传需要项目控制台与设备验收。

业务从 `FirebaseManager` 初始化，按需 `CreateAnalytics` / `CreateAuthBackend` / `CreateCrashReporter` / `CreateRemoteConfig` / `CreateMessaging`；统计交给 `AnalyticsManager`，账号交给 `AuthManager`。详见[用途清单](../../Documentation/SdkGuide.zh-CN.md)。
