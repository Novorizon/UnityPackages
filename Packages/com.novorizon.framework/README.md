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

## 单例启停

MonoSingleton<T> 的 OnStart 在一次运行中只执行一次，OnExit 负责对应的退出清理。重复访问 Instance 不重复启动；Dispose、Release、Unity 销毁或退出共用同一退出钩子。

```csharp
protected override void OnStart()
{
    LocalStorageManager.Instance.Initialize();
}

protected override void OnExit()
{
    LocalStorageManager.Release();
}
```

上例的 LocalStorageManager 位于 Game.Framework.Storage。释放已有单例时推荐 XxxManager.Release()，它不会通过 Instance 创建未使用的实例；已经取得实例也可调用 Dispose。ConfigurableSingleton<T> 同样提供 Release，内部清理 IDisposable 并保留退出保护。

初始化状态和重复释放由组件内部处理。GameStarter 在 OnStart 直接准备服务、在 OnExit 直接释放，不维护 Manager 所有权或跨服务初始化失败回滚。

## 自动单例重置

Singleton<T>、ConfigurableSingleton<T>、MonoSingleton<T> 首次使用时自动登记类型级回调。UnityFrameworkBootstrap 是唯一的 SubsystemRegistration 入口：新一次 Play 开始前先关闭项目入口和有顺序要求的模块，再清理已有实例、重置引用。退出时统一派发已有实例的清理，不会为清理创建对象。

登记表跨 Play 保留；关闭 Domain Reload 后无需业务逐类添加 Unity 启动特性。普通子类无需实现 Reset。普通对象实现 IDisposable，组件使用已有 OnExit；少数额外静态状态由模块显式登记，框架不通过反射猜测字段初值。MonoSingleton 保留场景组件的重启由 OnStart/OnExit 与运行代次共同处理。

SingletonLifecycle.Register(reset, shutdown, order) 面向框架和适配模块，order 较小者先执行。项目入口先于全局服务，ECS 自己保持 World -> Entity -> Pool 的清理顺序；Framework 不引用 ECS、UI、资源等独立包。模块回调不得在清理期间新建单例。全局保护只覆盖单例生命周期，不意味着各服务的业务方法都可跨线程调用。

启动回调只能重置框架掌握的状态，不能安全地自动重置子类的所有 static 字段。配置重载也不需要重建整个管理器。此处的自动重置用于新一次 Unity 运行及退出清理。
