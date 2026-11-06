# Framework Demo

离线基础服务仪表盘，面向 Unity 2022.3。不需要 UI 包、资源包、服务器或外部 Prefab。

1. Package Manager → Novorizon Framework → Samples → Import **Framework Demo**。
2. Tools → Novorizon → Demos → **Create Core Demo Scene**。菜单会处理未保存场景，并创建唯一名称的独立场景。
3. Play，在 Game 视图操作按钮；也可在空场景挂 FrameworkDemo。

| 分区 | 验证 |
| --- | --- |
| Typed events | 订阅、发布、退订，只修改演示计数 |
| Timers | scaled/unscaled 各一个句柄，暂停游戏时间后只有 unscaled 继续 |
| Reference pool | Rent、Reset 和 Return，归还时移除对象引用 |
| GameObject pool | 创建、回收和复用 marker，观察活跃/缓存数量 |
| Thread dispatch | 工作线程通过 UnityThread 回到主线程 |
| TaskUtil | 显式完成和取消等待 |
| Singleton | DemoSession 在同一 Play 中复用实例；下一次 Play 重新计数 |
| Logging | 显式输出演示日志 |

Stop、禁用组件或退出场景会清理自己的订阅、句柄、任务和池，并恢复自己修改的暂停状态。不会清空全局事件/定时器或关闭其他消费者的服务。

示例使用 IMGUI，界面重绘与状态字符串会分配，不作为性能基准。运行时代码位于 Runtime，创建场景菜单位于 Editor。网络协议由游戏提供，演示不带账号、进度、存档或奖励结算。
