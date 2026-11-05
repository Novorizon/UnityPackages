# 事件、消息与订阅

命名空间 Game.Framework.Event，入口 EventManager.Instance。它处理客户端内部通知，不负责 TCP 收发、UI 导航或业务数值计算。

## 带数据事件的完整例子

主题由游戏定义为 enum，不同枚举类型即使数值相同也独立。payload 实现本地的 Game.Framework.Event.IMessage，不是 Google.Protobuf.IMessage：

```csharp
using Game.Framework.Event;
using UnityEngine;

public enum WalletEvent { Changed, LoggedOut }

public sealed class CoinsChanged : IMessage
{
    public int Coins { get; }
    public CoinsChanged(int coins) { Coins = coins; }
}

public sealed class WalletEventView : MonoBehaviour
{
    readonly Subscriber subscriptions = new Subscriber();

    void OnEnable()
    {
        subscriptions.Add(EventManager.Instance.Subscribe<WalletEvent, CoinsChanged>(
            WalletEvent.Changed, OnCoinsChanged));
        subscriptions.Add(EventManager.Instance.Subscribe(WalletEvent.LoggedOut, OnLoggedOut));
    }

    void OnDisable() => subscriptions.Clear();
    void OnDestroy() => subscriptions.Dispose();
    void OnCoinsChanged(CoinsChanged message) => Debug.Log("Coins: " + message.Coins);
    void OnLoggedOut() => Debug.Log("Logged out");

    public void PublishExample()
    {
        EventManager.Instance.Notify(WalletEvent.Changed, new CoinsChanged(100));
        EventManager.Instance.Notify(WalletEvent.LoggedOut);
    }
}
```

实际钱包先完成业务计算/保存，再发布快照；PublishExample 只是通知用法。事件回调不应成为经济结算的唯一入口。

无参事件用 Subscribe(topic, Action) / Notify(topic)。带数据事件按 payload 的声明类型分别注册，无参和带数据互不替代；发布为基类/接口也不会自动通知所有派生类型监听。

## 过滤与拥有者

支持普通过滤方法，无须临时 lambda；以下方法放入已有业务类：

```csharp
bool IsValidChange(CoinsChanged value) => value.Coins >= 0;

void SubscribeWallet(Subscriber owner)
{
    owner.Add(EventManager.Instance.Subscribe<WalletEvent, CoinsChanged>(
        WalletEvent.Changed, IsValidChange, OnCoinsChanged));
}
```

无 payload 的重载也支持 `Func<bool>`。Subscribe 返回 ISubscription；每次注册独立拥有，重复注册同一方法会产生多份回调，应分别释放 token。

Subscriber.Clear 解除当前订阅但集合仍可重用，适合 OnDisable；Dispose 终结拥有者，之后 Add 会立即释放新订阅。少量订阅直接持有 token 也可以。

UI 包的 UIView 可用 `Track(EventManager.Instance.Subscribe(...))` 自动管理本次打开的订阅，不会让主包依赖 UI。

## 线程与快照

- 主线程发布同步执行监听者；后台发布通过共享 UnityThread 队列回主线程，宿主须已初始化。
- 每次发布取得稳定订阅快照。回调中新注册影响后续发布，不插入当前快照。
- 退订会失活该注册，已排队但未执行的通知跳过它；无法撤销已经执行中的业务代码。
- 单个监听者/过滤器异常记日志，其他有效监听继续。
- 不复制 payload。后台排队后不要修改或立即回收同一消息，推荐不可变快照。

注册表线程安全不等于业务字段线程安全。业务需要跨多次操作的顺序时，应自行维护状态顺序。

## 常见问题

| 现象 | 检查 |
| --- | --- |
| 每次打开回调增多 | Subscribe 后是否遗漏 Dispose/Clear |
| 收不到通知 | enum 类型、值和 payload 声明类型是否一致 |
| 后台发布提示未初始化 | 是否早于 Unity 启动，独立宿主是否初始化 UnityThread |
| 页面关闭还被引用 | 事件为强引用，是否遗漏退订或其他业务引用 |
| lambda 不好按方法退订 | 保存 ISubscription，不用新 lambda 替代原委托 |

优先释放自己拥有的 token。Unsubscribe(topic) 移除该主题全部 payload，Clear 清空整个全局表，普通页面不调用这些全局清理。

旧 Messager 是共享 EventManager 后台的迁移门面，Dispatcher/MessageDispatcher 转发 UnityThread。新业务用当前入口。MessageBus 提供调用方拥有的同步类型消息；UIMessageBus 复用该实现。它与全局 EventManager 保持各自的作用域与派发语义。
