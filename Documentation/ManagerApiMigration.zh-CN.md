# Manager 静态 API 迁移

| 类型 | 旧用法 | 新用法 | 生命周期 |
| --- | --- | --- | --- |
| LocalStorage | `LocalStorageManager.Instance.Initialize()`、`LocalStorageManager.Instance.LoadJson<T>(key)` | `LocalStorage.Initialize()`、`LocalStorage.LoadJson<T>(key)` | `LocalStorage.Reset()` 解除目录配置。已保存的文件保留，下次可指定新目录。 |
| DataManager | `DataManager.Instance.LoadAsync(...)` | `DataManager.Initialize(loader)`、`DataManager.LoadAsync(...)`、`DataManager.Get<T>()` | `DataManager.Reset()` 取消旧请求并清空快照。重新初始化创建新令牌，晚到结果无法发布。 |
| EventManager | `EventManager.Instance.Subscribe(...)`、`Instance.Notify(...)` | `EventManager.Subscribe(...)`、`EventManager.Notify(...)` | 订阅由创建它的实例持有，使用方 Dispose 返回的订阅。 |
| TimerManager | `TimerManager.Instance.Schedule(...)`、`Instance.Repeat(...)` | `TimerManager.Schedule(...)`、`TimerManager.Repeat(...)` | `TimerHandle` 绑定创建它的实例。Unity 驱动只推进已存在的计时器实例。 |
| InputManager | `InputManager.Instance.Subscribe(...)`、`Instance.RebindAsync(...)` | `InputManager.Subscribe(...)`、`InputManager.RebindAsync(...)` | 入口仍通过 `InputManager.Instance.Initialize(source, initialMap)` 指定配置。调用方释放屏蔽租约和订阅。 |
| AudioManager | `AudioManager.Instance.Play(...)`、`Instance.MasterVolume` | `AudioManager.Play(...)`、`AudioManager.MasterVolume` | `AudioHandle` 绑定创建它的组件及本次播放。 |
| UIManager | `UIManager.Instance.Pages`、`Instance.SetAssetLoader(...)`、`Instance.ShutdownAsync()` | `UIManager.Pages`、`UIManager.SetAssetLoader(...)`、`UIManager.ShutdownAsync()` | 现有组件上的重复关闭共享清理任务。页面、弹窗和 `UIHandle` 沿用原有所有权。 |
| GpuAnimatorManager | `GpuAnimatorManager.Instance.Tick(...)`、`Instance.AnimationChanged` | `GpuAnimatorManager.Tick(...)`、`GpuAnimatorManager.SubscribeAnimationChanged(listener)` / `UnsubscribeAnimationChanged(listener)` | 注册及事件仍由组件持有。播放器禁用时的注销不会创建组件。 |
| ResourceManager | `ResourceManager.LoadAssetAsync<T>(...)` | `ResourceManager.LoadAssetAsync<T>(...)` | 每次成功加载配对一次 `ResourceManager.Release(asset)`。 |

GameStarter 的装配代码可以继续用 `Instance.Initialize()` 初始化仍继承单例基类的类型。集中销毁中的 `DisposeManager<DataManager>()` 和 `DisposeManager<LocalStorageManager>()` 分别换成 `DataManager.Reset()` 和 `LocalStorage.Reset()`。原先针对存储的 `TryGetInstance` 检查可改为检查 `LocalStorage.RootDirectory`。

## GameStarter 的 SDK 依赖

| 嵌入包 | 当前 package.json 版本 |
| --- | --- |
| com.novorizon.framework | 0.18.0-preview.9 |
| com.novorizon.data | 0.2.0-preview.9 |
| com.novorizon.input | 0.1.0-preview.14 |
| com.novorizon.audio | 0.2.0-preview.13 |
| com.novorizon.framework.ui | 0.11.0-preview.2 |
| com.novorizon.gpuanimator | 4.0.0-preview.19 |
| com.novorizon.diagnostics | 0.2.0-preview.2 |

GameStarter 应将整套嵌入 SDK 一起固定到这次交付的提交。表格列的是包清单版本，嵌入 SDK 以 Git SHA 区分。

这次迁移改变 AOT 公共签名。先用新 SDK 重新生成目标平台 HybridCLR 的裁剪后 AOT 元数据和方法桥，再同批构建新 Player 和 HotGame DLL。已有 Player 与新 HotGame DLL 混用可能出现 `MissingMethodException` 或 `TypeLoadException`。
