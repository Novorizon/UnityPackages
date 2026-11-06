# 任务工具、日志与单例

命名空间 Game.Framework。纯托管工具在 Core，MonoSingleton 的 Unity 支持在 Game.Framework.Unity。

## TaskUtil.WaitAsync

```csharp
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;

public static class WaitExample
{
    public static async Task<int> ReadAsync(Task<int> sharedLoad, CancellationToken pageToken)
    {
        int result = await TaskUtil.WaitAsync(sharedLoad, pageToken);
        pageToken.ThrowIfCancellationRequested();
        return result;
    }
}
```

只结束本次等待，不强制停止传入任务，也不取消其他等待者；完成后释放取消注册，取消后观察迟到故障。底层支持取消时，还应把相应 token 传给它自己的 API。

它不访问 Unity，也不保证切到主线程，需要时使用 [UnityThread](Threading.md)。不要用 Wait/Result 阻塞主线程。

## Forget 与一次性 Signal

按钮边界可写 `RefreshAsync().Forget()`，文件引用 Game.Framework。Forget 忽略 OCE，其他错误记 Logger；它不取消任务，常规业务链优先返回并 await Task。

以下放在 async 业务方法中：

```csharp
var ready = new Game.Framework.Signal();
Task waiting = ready.WaitAsync(token);
ready.SetTrue();
await waiting;
```

SetTrue 可重复调用，一旦置位后续等待立即完成；没有 Reset，不用于循环广播。WaitSignalAsync(token) 固定等五秒，成功 true、自身超时 false、调用方取消仍 OCE；WaitAsync 本身没有五秒超时。

## 日志

```csharp
using Game.Framework;

Logger.Info("Loading profile");
Logger.Warn("Cached profile is stale");
Logger.Error("Profile load failed");
// catch(Exception error) 中：Logger.Exception(error);
```

Unity 启动后输出 Unity Console，独立 Core 默认 Console。Warn/Error/Exception 不会因发布配置消失。

直接 Logger.Info 使用 Conditional("ENABLE_DEBUG_LOG")，调用方未定义符号时调用及参数表达式被移除。ILogger/NetLogger 转发受转发代码编译条件影响，不能假定所有接口调用都不构造字符串；必要业务逻辑不要写进日志参数。

后端实现四个方法，并在应用初始化时安装：

```csharp
using System;
using Game.Framework;

public sealed class ProjectLogger : ILogger
{
    public void Info(string message) => Console.WriteLine(message);
    public void Warn(string message) => Console.Error.WriteLine(message);
    public void Error(string message) => Console.Error.WriteLine(message);
    public void Exception(Exception error) => Console.Error.WriteLine(error);
}

// 应用初始化：Logger.Sink = new ProjectLogger();
```

Sink 是全局配置，普通页面不反复替换。日志可能来自后台，自定义后端应适当同步，不在后台修改 UI，也不要在日志回调中继续抛异常。NetLogger 转发同一个后端，不是另一套日志。

## 托管单例：`Singleton<T>`

```csharp
using Game.Framework;

public sealed class ClientPreferences : Singleton<ClientPreferences>
{
    public bool MusicEnabled { get; set; } = true;
}

// ClientPreferences.Instance.MusicEnabled = false;
```

Lazy 保证共享入口初始化线程安全，IsCreated 不触发构造。类型须满足 public 无参构造，因此基类不禁止外部 new，也不令业务字段自动线程安全。

实例在进程/AppDomain 保留，没有通用 Reset；禁用 Domain Reload 后业务状态可能跨 Play 保留。账号/会话状态应显式清理，构造中避免 Unity API、联网和资源初始化。

NetworkManager 使用自己的应用生命周期与封闭构造入口，普通页面直接 Instance，不 new 或 Dispose 全局连接。

## Unity 单例：`MonoSingleton<T>`

```csharp
using Game.Framework;
using UnityEngine;

public sealed class SoundService : MonoSingleton<SoundService>
{
    public void PlayNotification() { Debug.Log("Play notification"); }
}

// Awake/Start或之后的主线程：SoundService.Instance.PlayNotification();
```

Instance 先找场景组件，找不到才创建；根 GameObject 跨场景保留。放在场景根，不挂在短暂 UI 子节点下。

访问需要已初始化主线程，不能从后台借 Instance 创建 GameObject；退出期间返回 null，不重建。自定义 Awake/OnDestroy/OnApplicationQuit 要保留 base 调用；重复物体被销毁后，其业务初始化也应停止继续执行。

框架新 Play 重置静态入口，存活场景对象仍由 Unity 生命周期决定；关闭 Domain Reload/Scene Reload 的真实组合需项目验证。

## 旧接口迁移

Dispatcher/MessageDispatcher 使用共享 UnityThread，Messager 使用共享 EventManager。新代码使用 [Threading](Threading.md) 与 [Events](Events.md)，不新建重复队列。当前 Signal 位于 Game.Framework，注意与旧同名类型区分。

## MonoSingleton 启动回调

Framework 0.10.0-preview.2 起，派生类重写 `protected override void OnStart()` 编写启动逻辑。旧回调 `OnSingletonAccessed` 改名为 `OnStart`，项目中的重写也需要同步改名。UI、ECS 与 HybridCLR 封装已同步。

此回调由 Framework 在单例 Awake 或访问 Instance 时触发；派生类应通过自己的初始化状态避免重复执行工作。它与 Unity 的 Start 生命周期方法分别由各自调用方触发。

## 可配置、可重置的服务单例

`Singleton<T>` 保持原有 Lazy 无参创建行为。需要工厂配置和运行重置的服务可继承 `ConfigurableSingleton<T>`：派生类用 GetOrCreateInstance 提供默认工厂，用 ConfigureInstance 在首次取实例前配置服务，用 ResetInstance 显式清理并重置，用 ReleaseInstance 按实例身份清除引用。基类提供 IsCreated 和 TryGetInstance，查询不会创建对象。

ResetInstance 在锁外清理旧实例，清理期间拒绝创建；stopping=true 阻止退出时重新创建，显式重置后可开始新的运行。网络参数、认证与连接仍由 NetworkManager 校验和处理，NetworkManager 已复用此基类。


单例基类提供无参 virtual Initialize，用于在项目入口集中显式准备服务。有实际无参初始化逻辑时 override；需要参数时新增重载，并可让无参 override 委托到默认参数配置。这个入口不重新调用 MonoSingleton.OnStart，也不代替需要 await 的异步初始化。
