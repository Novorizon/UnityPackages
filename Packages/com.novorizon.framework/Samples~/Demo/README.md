# Core Demo：离线基础服务仪表盘

适用 Unity 2022.3。只需主包，不依赖 UI 包、YooAsset、服务器或外部 Prefab；使用 IMGUI 显示可滚动仪表盘，不要求项目启用旧 Input Manager。示例仅按需导入，导入本身不会建场景或联网。

## 运行

1. Package Manager 选择 Novorizon Framework，在 Samples 导入 **Framework Demo**。
2. 点击 **Tools > Novorizon > Demos > Create Core Demo Scene**。菜单会先询问保存当前修改的场景，再新建独立场景，并保存到 `Assets/NovorizonDemos/CoreDemo.unity`；已有同名文件时自动选择新文件名。
3. 点击 Play，即可操作仪表盘。手机或小窗口可滚动；示例界面文本采用英文，便于使用项目默认 IMGUI 字体。
4. Stop and clean up 或离开场景会取消本 Demo 的任务、计时器和订阅，释放本 Demo 的池；不会清空全局事件、全局定时器或关闭其他页面共享的网络连接。

也可在空场景的新 GameObject 上添加 `FrameworkDemo`，Play 后自动启动。不要把这个演示控制器挂到正式游戏场景中。

## 按钮与预期

| 分区 | 操作 | 预期 |
| --- | --- | --- |
| Typed events | Publish +1 | 订阅期间收到类型化消息，received 与 score 增加。Unsubscribe 后发布不再修改这份仪表盘；Subscribe 恢复。 |
| Timers | Start timers | scaled/unscaled 每秒各触发一次。Pause game time 后 scaled 停止，unscaled 继续；Resume 恢复。Cancel timers 只取消本 Demo 的两个句柄。 |
| Production | Simulate missed-period settlement | 不阻塞主线程；独立结算器直接按 5.25 秒经过的 5 个周期结算 25 单位，同一时刻再次结算为 0。真实生产计数也按经过周期计算，不用 timer 回调次数发奖励。 |
| Reference pool | Rent / use / return | 租借、填写业务字段、在 finally 归还；下次租借时 Reset 检查为 true，cached 不超过容量。 |
| GameObject pool | Spawn/Despawn | 创建/复用空 marker，在 Hierarchy 观察 `Core Demo Pooled Item`。显示 active/cached 计数，最多 16 个活跃实例、8 个空闲缓存；组件显示 OnSpawn/OnDespawn 计数。无需材质、相机渲染管线或外部模型。 |
| Thread dispatch | Worker -> UnityThread.RunAsync | 工作线程不访问 Unity 对象；回到主线程修改状态，main callbacks 增加。离开 Demo 后旧回调不能更新新一轮会话。 |
| TaskUtil | Start wait / Complete / Cancel | 等待本 Demo 的手动信号，不进行 IO。Complete 后任务完成，Cancel 后任务取消；重复开始会取消旧等待。 |
| Singleton | visits this domain | `DemoSession.Instance` 展示纯 C# lazy 单例计数；同一脚本域内 Stop/Start 会增加次数，禁用 Domain Reload 时不承诺自动清零。 |
| Logging | Write example log | Console 输出明确标注的示例 Warn；Info 仅在 `ENABLE_DEBUG_LOG` 开启时存在。 |

Stop 后可以 Start 再次运行；暂停按钮产生的 timeScale 修改会在恢复/退出时还原，若其他系统已把 timeScale 改成非零值，则不覆盖它。

## 入口与阅读源码

- `Runtime/FrameworkDemo.cs`：业务操作、状态和所有权清理；无手工 `network.Update` 或 `TimerManager.Update`。
- `DemoProductionLedger.cs`：纯业务结算示例，可独立验证漏过多个周期、不重复结算与溢出保护。
- `DemoReferenceContext.cs`：归还时清除 GameObject 引用。
- `CoreDemoPooledItem.cs`：池回调重置。
- `DemoSession.cs`：只持有演示计数的单例，不保存账号状态。
- `Editor/FrameworkDemoSceneMenu.cs`：用户主动创建场景；编辑器程序集不会进入 Player。

此 Demo 不是性能基准：IMGUI 重绘、字符串格式化、按钮临时闭包和演示任务会产生正常分配。池、线程和定时器的具体性能应在真实项目与设备上测量。

生产结算器仅演示“经过周期数减去已结算周期数”的计算方式；使用与定时器一致的有限浮点边界容差，处理 0.1 秒在 0.3 秒附近的舍入。商业游戏应由服务器和持久化状态确认奖励，不能把本地演示时钟当作账号资产的权威来源。

网络请另导入主包 **TCP Network** 示例，按其 README 配置服务器、认证与加密，并显式连接。Core Demo 不读取或保存 JWT，不自动联网，不写服务器数据。完整 UI 演示由独立 UI 包的 UI Demo 提供。
