# 定时器与对象池脚本示例

这是需要自行绑定 Prefab 的最小片段；无需外部 Prefab 的完整离线仪表盘请 Import 主包 **Framework Demo**。

1. 挂 `Game.Framework.Samples.FoundationServicesExample`，在 Inspector 指定项目自己的 Prefab。
2. 从测试按钮或业务代码显式调用 SpawnExample，不会在导入或 Start 中自动生成实例。
3. 示例租借伤害上下文后立即归还，并在两秒后通过本实例的 TimerHandle 回收 GameObject。
4. 销毁拥有者会取消自己的计时器并 Dispose 自己的池，不清空全局服务。

[定时器](../../Documentation~/Timing.md) · [池](../../Documentation~/Pooling.md) · [完整 Demo](../Demo/README.md)
