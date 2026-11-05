# GameObject 池与引用池

命名空间 Game.Framework.Pooling，两种池用于不同对象，均由业务按生命周期拥有。

| 类型 | 内容 | 线程与结束 |
| --- | --- | --- |
| GameObjectPool | 预制体实例和原生组件 | Unity 主线程；单实例 Despawn，池拥有者 Dispose |
| `ReferencePool<T>` | 可 Reset 的普通 class | 池登记线程安全，租借者独占对象；Return |

池化增加检查和重置成本，创建昂贵或高频对象更适合池化；简单小对象 new 可能更快，不应强行池化所有 DTO 或 Protobuf 消息。

## GameObjectPool 完整例子

```csharp
using Game.Framework.Pooling;
using UnityEngine;

public sealed class ProjectilePoolOwner : MonoBehaviour
{
    [SerializeField] GameObject prefab;
    GameObjectPool pool;
    GameObject current;

    void Start() => pool = new GameObjectPool(prefab, maximumRetained: 64, prewarm: 8);

    public void SpawnOne()
    {
        ReturnOne();
        current = pool.Spawn(transform);
        // 也可 pool.Spawn(worldPosition, worldRotation, parent)。
    }

    public void ReturnOne()
    {
        if (current == null) return;
        GameObject instance = current;
        current = null;
        pool.Despawn(instance);
    }

    void OnDestroy()
    {
        current = null;
        pool?.Dispose();
    }
}
```

挂在场景物体并指定 Prefab，在 Start 完成后调用按钮方法。每池一个 Prefab，MaximumRetained 只限制失活缓存，不限制活跃数量；发射器另设业务活跃上限。超额归还销毁多余实例。

失活根跨场景保留。Spawn 不指定 parent 时放入当前活动场景，指定时遵循父物体场景。归还后不能继续修改实例，旧异步回调也不能把它当成原来的租借。

## 重置回调

```csharp
using Game.Framework.Pooling;
using UnityEngine;

public sealed class PooledProjectile : MonoBehaviour, IGameObjectPoolCallbacks
{
    public float Age { get; private set; }
    public void OnSpawn() { Age = 0; }
    public void OnDespawn() { Age = 0; }
}
```

还应清理粒子、拖尾、目标、订阅和该次租借的计时器。位置/旋转/缩放在激活前设置，之后才 OnSpawn；OnDespawn 在失活前执行。回调列表创建时缓存，包含子物体，不要运行期增删这些组件。

不能在同一实例 OnSpawn/OnDespawn 内再次回收自己；重复归还、错误归属会抛异常。回调抛错时实例销毁，不把损坏状态放回缓存。外部销毁的已激活实例也会清登记，正常业务仍用 Despawn。

## 分批预热

Prewarm(count) 与构造参数同步创建，count 是目标空闲数，不是额外创建量，不能超过 MaximumRetained。加载阶段可以：

```csharp
await pool.PrewarmAsync(64, maximumPerFrame: 4, ct: token);
```

首批立即执行，后续经主线程队列逐批，每个请求每批至多指定数量。各请求分别计预算，不是整个应用共同的帧上限；手动多次推进派发器也可能在一个实际帧执行多批。

取消在批次边界检查，Dispose 结束本池预热等待，已创建的空闲实例保留至 Trim/Clear/Dispose。多个复杂池应统一规划预热，而不是同时设置高预算。

## 清理与资源

- Trim(n) 保留至多 n 个空闲实例，不影响活跃实例。
- Clear 清空闲缓存，等同 Trim(0)。
- Dispose 清整个池，包括活跃实例；此后不再使用。
- CountActive / CountInactive 在主线程读取。

池不加载或卸载资源，不绑定 YooAsset/Addressables。项目保持 Prefab 租约，直至池停止使用且所有实例真正销毁；Dispose/Clear 发出 Destroy 请求不代表原生对象已立刻消失，不能刚返回就卸载共享 Prefab/纹理。

应用退出/新 Play 会话有兜底清理，普通功能仍应自行 Dispose。

## 托管引用池：`ReferencePool<T>`

类型须是 class、有 public 无参构造并实现 IReferencePoolable.Reset。Reset 清理全部业务引用和状态：

```csharp
using Game.Framework.Pooling;

public sealed class DamageContext : IReferencePoolable
{
    public object Target;
    public int Damage;
    public void Reset() { Target = null; Damage = 0; }
}

public static class DamageExample
{
    public static int Calculate(object target, int damage)
    {
        var pool = ReferencePool<DamageContext>.Shared;
        DamageContext context = pool.Rent();
        try
        {
            context.Target = target;
            context.Damage = damage;
            return context.Damage;
        }
        finally { pool.Return(context); }
    }
}
```

Shared 默认至多保留 256 个空闲对象。隔离模块或调整容量可 `new ReferencePool<DamageContext>(maximumRetained: 128)`。对象按身份校验，拒绝外来或重复归还；Return 后不能再访问。

| API | 含义 |
| --- | --- |
| Rent / Return | 获取当前独占所有权，归还时 Reset |
| Prewarm(n) | 准备目标空闲数，不超过 MaximumRetained |
| Trim(n) | 收缩空闲缓存 |
| Clear | 清缓存并换代，旧代归还仍 Reset，但不再缓存 |
| CountInactive | 当前空闲数量 |
| ReferencePool.ClearAll | 清已经创建的所有 Shared；独立池自己 Clear |

Reset 在锁外运行，失败会淘汰该对象并向归还者抛错。弱键登记不会永久留住已无其他引用的普通对象，但忘记归还仍是拥有者错误。

池线程安全不代表同一租借对象可被多线程随意访问。它不自动 Dispose 文件、资源句柄或连接，也不保证第三方对象适合池化。
