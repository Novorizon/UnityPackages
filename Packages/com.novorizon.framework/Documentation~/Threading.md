# 主线程与异步流程

命名空间 Game.Framework，程序集 Game.Framework.Core。Unity 集成由 Game.Framework.Unity 自动安装，正常业务无需手动 Update。

| 方法 | 用途 | 完成语义 |
| --- | --- | --- |
| UnityThread.Post(Action) | 提交无需等待的主线程操作 | 入队返回，回调异常记日志 |
| UnityThread.TryPost(Action) | 队列满时交给业务处理 | 满时 false，未初始化仍抛异常 |
| UnityThread.RunAsync(Action, ct) | 在主线程执行操作并等待 | 操作异常、取消进入返回的 Task |
| await UnityThread.SwitchToMainThread(ct) | 将后续执行切到主线程 | await 后在绑定主线程继续 |

Post 即使从主线程调用也会入队。RunAsync 已在主线程时立即执行 Action；等待它只保证 Action 的线程，不应当作后续代码全部切换线程的承诺。需要这种语义时使用 SwitchToMainThread。

## 完整组件例子

挂在场景物体上，从按钮调用 Rebuild。后台只计算普通托管数据，在明确切回主线程后修改 Transform：

```csharp
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using UnityEngine;

public sealed class MainThreadWorkExample : MonoBehaviour
{
    readonly CancellationTokenSource lifetime = new CancellationTokenSource();
    public void Rebuild() => RebuildAsync(lifetime.Token).Forget();

    async Task RebuildAsync(CancellationToken token)
    {
        int value = await Task.Run(() => Calculate(token), token);
        await UnityThread.SwitchToMainThread(token);
        token.ThrowIfCancellationRequested();
        transform.localPosition = new Vector3(0, value % 7, 0);
    }

    static int Calculate(CancellationToken token)
    {
        int result = 0;
        for (int i = 0; i < 10000; i++)
        {
            if ((i & 255) == 0) token.ThrowIfCancellationRequested();
            result += i;
        }
        return result;
    }

    void OnDestroy()
    {
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
```

简单操作也可放在已有 async 业务方法中：

```csharp
await UnityThread.RunAsync(() => transform.localScale = Vector3.one, token);
```

不要在后台读取 GameObject、Transform、Resources 等需要主线程的 Unity API。

## 取消、异常与预算

- RunAsync 可拒绝尚未执行的操作，不能中断已开始的同步 Action；较长计算仍应检查 token 或拆分。
- SwitchToMainThread 在恢复时检查取消。取消等待不等于底层工作已经停止。
- 等待型操作由 await 传播异常，Post 的回调异常交给 Logger。
- Forget 适合按钮等不能返回 Task 的边界：忽略取消、记录其他异常。常规业务链应返回并 await Task。
- 不要在主线程用 Wait/Result 阻塞需要主线程继续的任务。

共享队列最多 4096 项，每批默认最多执行 64 项。存在待处理工作且当前未安排 Drain 时才 Post 上下文，不是每个回调各 Post 一次。预算按次数，不按毫秒；一个昂贵 Action 仍会卡帧。

高频生产者应合并同对象刷新、缓存常用委托，并明确处理 TryPost 的 false。不能静默丢弃必须结算的业务事务。IsInitialized、IsMainThread、PendingCount 可用于诊断。

Reset 属于宿主切换/退出，会废弃队列并取消等待型操作；普通页面不重置全局派发器。

## 独立 .NET 宿主

在选定线程 InitializeManual，再由该线程周期调用 UnityThread.Update。已有宿主不能在不同线程或上下文重复绑定；先结束旧任务并 Reset。手动派发不自动驱动计时器，后者见 [Timing](Timing.md)。正常 Unity 使用自动方式，不混用手动推进。
