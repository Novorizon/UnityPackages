# 定时器与周期结算

命名空间 Game.Framework.Timing，入口 TimerManager.Instance。Unity 用一个共享 Update 驱动，不为每个计时器创建线程、Task 或协程，业务不调用 Update。

## 延迟、循环和取消

以下放在功能开启方法，保存句柄并在功能退出时取消：

```csharp
using Game.Framework.Timing;

TimerHandle delay = TimerManager.Instance.Schedule(2, RefreshUi);
TimerHandle countdown = TimerManager.Instance.Repeat(1, RefreshCountdown, useUnscaledTime: true);
TimerHandle polling = TimerManager.Instance.Repeat(1, Poll, useUnscaledTime: true,
    mode: TimerRepeatMode.FixedDelay);

delay.Cancel();
countdown.Dispose();
polling.Dispose();
```

RefreshUi、RefreshCountdown、Poll 是游戏的无参方法。TimerHandle 为 IDisposable 结构体，有 IsActive；副本仍指向同一任务，重复取消安全，旧句柄不会取消新任务。

| 时间/模式 | 行为 | 用途 |
| --- | --- | --- |
| 默认缩放时间 | 随 timeScale 暂停或变速 | 战斗技能、游戏内效果 |
| useUnscaledTime=true | 真实经过时间 | UI 倒计时刷新、真实时间轮询 |
| FixedRate（默认） | 保持原始周期相位，错过触发跳过 | 定期检查和刷新 |
| FixedDelay | 回调结束后再等完整间隔 | 避免执行后立刻再次轮询 |

Unity 绑定 Time.timeAsDouble 与 Time.realtimeSinceStartupAsDouble，按绝对到期点比较。真实经过时间不是日期时间，不依赖时区；缩放时钟在同一帧保持稳定，需要按实际执行耗时延迟的轮询选 unscaled。

例如周期 0.1 秒，卡到 0.35 秒才执行：FixedRate 只执行一次，下一次按原相位在 0.4 秒；FixedDelay 从执行结束后再等 0.1 秒。循环项每次推进最多触发一次，不会补发大量回调。

Schedule(0) 最早在下一次推进执行；回调中新建的零延迟任务也留到后续推进。Repeat 间隔须大于零，时长须有限、非负。

## 周期产出按经过周期结算

定时器负责唤醒，业务负责结算。每 5 秒产出 10 金币，最后结算至第 1 周期、恢复时已过第 4 周期，应一次结算 3 周期，不能只按收到的一次回调发 10。

```text
已完成周期 = floor((当前时间 - 业务起点) / 产出周期)
待结算周期 = 已完成周期 - 已结算周期
本次产出   = 待结算周期 × 每周期产出
```

完整单会话示例如下。组件由生产业务拥有，不挂在频繁关闭的展示页面上：

```csharp
using System;
using Game.Framework.Timing;
using UnityEngine;

public sealed class PeriodicProductionExample : MonoBehaviour
{
    const double PeriodSeconds = 5;
    const long CoinsPerPeriod = 10;
    double origin;
    long settledPeriods;
    bool initialized;
    TimerHandle wakeup;
    public long TotalCoins { get; private set; }

    void Start()
    {
        origin = Time.realtimeSinceStartupAsDouble;
        initialized = true;
        wakeup = TimerManager.Instance.Repeat(0.5, Settle, useUnscaledTime: true);
    }

    public void Settle()
    {
        if (!initialized) return;
        double elapsed = Math.Max(0, Time.realtimeSinceStartupAsDouble - origin);
        long completed = checked((long)Math.Floor(elapsed / PeriodSeconds));
        long due = completed - settledPeriods;
        if (due <= 0) return;
        long earned = checked(due * CoinsPerPeriod);
        long nextBalance = checked(TotalCoins + earned);
        // 正式业务在此幂等保存成功后，再推进结算游标。
        TotalCoins = nextBalance;
        settledPeriods = completed;
    }

    void OnDestroy() => wakeup.Cancel();
}
```

起点后 16 秒首次结算得到 3 周期、30 金币；同一时刻重复结算不再发放；到 25 秒再结算剩余 2 周期。唤醒间隔可以远小于产出周期，也可以在用户打开界面时直接 Settle。

持久化/离线产出还需保存业务起点和游标，用事务或幂等业务操作一起推进。跨进程、跨设备使用可信时间源和整数时间单位；设备会话 realtime 不能当服务器时间，客户端不应成为商业游戏经济的最终裁决者。整数时间也更容易定义周期边界的舍入规则。

## 精度、预算与规模

实际回调受帧率、卡顿和积压影响；绝对时钟减少漂移，不提供音频或硬实时保证。切后台没有游戏循环时不执行回调，恢复后按时钟判断，目标平台仍需验证。

当前最多 65536 个任务，内部空闲记录最多缓存 1024，默认每次推进回调预算 256。CallbackBudget 可在主线程调整，预算只限制数量，不限制单个回调耗时。

两种时钟分别用带索引最小堆，空闲时检查堆顶，插入/取消通常 O(log n)，不逐帧扫描全部未到期项。回调异常记日志，其他任务继续；非法/倒退时钟或无法安排下次到期点会抛错，相关循环项会被取消。

Clear 取消全局任务，Clear(trimExcess: true) 进一步收缩缓存。普通页面只取消自己的句柄。

## 独立 .NET 宿主

先在宿主线程 UnityThread.InitializeManual，再周期调用 UnityThread.Update 与 TimerManager.Instance.Update(scaledDeltaSeconds, unscaledDeltaSeconds)。也可在无任务、非分发时 UseClocks(gameClock, realClock) 绑定有限、非负、单调秒数函数，再无参 Update；绑定后不混用 delta 重载。

Unity 正常运行已自动绑定时钟，不重新 UseClocks，也不手动推进第二次。
