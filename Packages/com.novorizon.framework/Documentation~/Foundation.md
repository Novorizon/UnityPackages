# 基础服务使用指南

适用 Unity 2022.3 / .NET Standard 2.1。基础服务随主包安装，UI 与资源适配分别按需安装。业务通常通过 Instance 或静态入口调用，无需自己搭建派发线程或逐帧驱动。

## 按需求选择模块

| 需求 | 入口 | 指南 |
| --- | --- | --- |
| 后台结果回到主线程 | UnityThread.Post / RunAsync / SwitchToMainThread | [线程与异步](Threading.md) |
| 可取消地等待已有任务 | TaskUtil.WaitAsync | [任务、日志与单例](Utilities.md) |
| 客户端内部业务事件 | EventManager.Instance | [事件与订阅](Events.md) |
| 延迟、循环、倒计时刷新 | TimerManager.Instance | [定时器与周期结算](Timing.md) |
| 预制体与普通对象复用 | GameObjectPool / `ReferencePool<T>` | [对象池](Pooling.md) |
| 日志和共享服务 | Logger / `Singleton<T>` / `MonoSingleton<T>` | [任务、日志与单例](Utilities.md) |
| 登录、请求、服务端通知 | NetworkManager.Instance | [网络指南](Network.md) |

Game.Framework.Core 不引用 UnityEngine，包含事件、线程队列、任务工具、定时器与引用池。Game.Framework.Unity 提供启动、自动时钟驱动、MonoSingleton 和 GameObjectPool，并引用 Core。业务 asmdef 按实际使用显式引用对应程序集。

## 初始化与退出

安装主包后，Unity 在场景加载前捕获主线程 SynchronizationContext、将日志接入 Unity Console，并为 TimerManager 安装一个共享 Update 驱动。正常 Awake/Start 或之后使用即可，不必自己调用 UnityThread.Initialize、UnityThread.Update 或 TimerManager.Update。

NetworkManager 另有显式 Initialize：它准备并校验网络配置，不会联网；ConnectAsync 才开始连接。完整用法见网络指南。

新 Play 会话会重置事件、定时器、共享引用池、GameObject 池登记和 MonoSingleton 静态入口。自动退出清理是兜底，各业务仍应及时清理自己拥有的工作。

| 创建的对象 | 通常如何结束 |
| --- | --- |
| ISubscription | 订阅的页面/功能关闭时 Dispose |
| Subscriber | Clear 可重用，Dispose 后不重用 |
| TimerHandle | 功能退出时 Cancel / Dispose |
| CancellationTokenSource | 结束流程时 Cancel，再 Dispose |
| GameObjectPool | 单个实例 Despawn；池的场景/功能拥有者 Dispose |
| 引用池租借对象 | try/finally 中 Return |
| 全局连接、日志和共享服务 | 应用/账号生命周期统一管理，普通页面不清空全局服务 |

UI 包的 UIView.Track 可接收 EventManager 返回的订阅，OpenToken 可传入业务异步方法；不需要把基础服务改成 UI 专用实现。只用主包时，MonoBehaviour 的 OnEnable/OnDisable/OnDestroy 也可管理拥有者生命周期。

## 示例与性能

导入主包的离线 [Core Demo](../Samples~/Demo/README.md) 后，菜单 `Tools / Novorizon / Demos / Create Core Demo Scene` 创建场景，Play 后可操作事件、定时器、周期产出、对象池、线程、任务、日志和单例。示例不要求网络或外部 Prefab。

Package Manager Samples 中的 Typed Events 展示小型本地事件组件，Timers and Pools 展示定时归还与池，TCP Network 展示显式登录和收发。网络操作仍须配置并主动连接。

示例中的枚举、业务对象和金币规则由游戏定义。先运行离线基础示例，再配置网络账号与服务器。

稳定事件发布、计时调度、缓存复用等路径减少了重复分配，但业务闭包、消息、字符串、扩容和原生 Instantiate 仍可能分配。池化也会增加重置与检查成本，少分配不代表 CPU 一定更快。

仓库历史 .NET 基准只说明指定提交、指定工作量，不是当前 Unity 帧率、常驻内存或手机性能保证。实际 Prefab、Unity Profiler、Memory Profiler 和目标平台构建仍是项目验收依据；历史数据保留在仓库 docs 与 tools。
