# Novorizon Framework

Unity 2022.3 / .NET Standard 2.1 基础库与 TCP / Protobuf 游戏客户端，适用 Windows、Android 和 iPhone。

## 先运行

1. 在本包 Samples 中 Import **Framework Demo**。
2. 菜单 **Tools / Novorizon / Demos / Create Core Demo Scene**。
3. 保存当前场景修改后进入新 Demo 场景，Play，在仪表盘操作事件、计时器、池、线程和异步示例。

不需要服务器或外部 Prefab。网络另 Import **TCP Network**，默认不会自动连接；UI 是独立可选包。

## 文档

- [安装、包选择与快速开始](Documentation~/Package.md)
- [基础服务总览](Documentation~/Foundation.md)
- [线程与异步](Documentation~/Threading.md)
- [事件](Documentation~/Events.md)
- [定时器与周期结算](Documentation~/Timing.md)
- [GameObject 池与引用池](Documentation~/Pooling.md)
- [Task、日志与单例](Documentation~/Utilities.md)
- [网络、认证与 Protobuf](Documentation~/Network.md)
- [基础 Demo 操作](Samples~/Demo/README.md)
- [网络 Demo 操作](Samples~/Network/README.md)

## 包边界

主包包含 Core、Unity 自动驱动、TCP 网络、Unity 网络配置及旧接口迁移门面。Google.Protobuf、BouncyCastle 与 Unsafe 运行时 DLL 已提供，项目每个同名程序集只保留一份。代码生成工具与合同测试不随运行时包导入。

NetworkManager.Instance 是应用级入口，Initialize 在主线程加载配置，不立即联网。页面只取消自己的请求和退订；不要销毁应用共享连接。事件、定时器、池的资源拥有者与清理方法见各模块文档。

UI 包依赖 Framework 与 uGUI，以 IUIAssetLoader 对接任意资源系统；YooAsset 适配包独立可选。不要同时导入 UPM 包与其 Assets 源码副本。自定义 asmdef 显式引用实际使用的程序集。

## 游戏服务

场景协调器为 `Game.Framework.Scenes.SceneManager`，支持 Build Settings 场景和可注入的 ISceneLoader；YooAsset 场景适配在独立 yooasset 包中。

`Game.Framework.Data.DataManager` 按类型持有运行时数据并管理释放顺序。`Game.Framework.Storage.LocalStorageManager` 提供 persistentDataPath 下的版本化 JSON/Text/Bytes 读写、临时文件替换与备份。它们不定义玩家存档字段。

音频 `com.novorizon.audio` 和配置 `com.novorizon.config` 是依赖 Framework 的独立包，可通过 framework.all 一起安装。

网络 StateChanged 提供派发后的状态通知。ConnectAsync 的取消只停止当前调用方等待，DisconnectAsync/DisposeAsync 才停止共享连接与重连。请求超时覆盖发送和响应，迟到响应不能污染后续请求，业务请求不自动重放。断开异常不跳过传输层释放。
