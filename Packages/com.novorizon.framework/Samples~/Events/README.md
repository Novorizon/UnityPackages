# 类型化事件脚本示例

此 Sample 是最小业务片段，不自动创建场景；完整可操作示例请 Import 主包的 **Framework Demo**。

1. 在场景 GameObject 挂 `Game.Examples.EventView`。
2. OnEnable 订阅示例主题，OnDisable 用 Subscriber.Clear 退订。
3. 在项目自己的测试按钮或代码调用 `await EventViewPublisher.SimulateAsync(playerId)`，它延迟后发布无参事件，不联网。
4. 带 payload 的业务示例见 [事件指南](../../Documentation~/Events.md)；EventView 中两个不同 payload 订阅不会互相替代。

此示例没有 IAP SDK 或真实支付流程；MessageTopic.IapFinish 是演示主题，不能作为支付成功验证。
