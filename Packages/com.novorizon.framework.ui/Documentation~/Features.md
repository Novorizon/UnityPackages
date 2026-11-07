# UI 0.5 业务功能

适用 Unity 2022.3，运行时依赖 Framework 和 uGUI，不依赖具体资源库。主入口为 `UI.UIManager.Instance`；所有 UI 方法在 Unity 主线程调用。支持 Windows、Android、iPhone 的源码接口，实际设备、Canvas、输入和 IL2CPP 仍需项目验证。

希望先体验完整操作时，导入 [UI Demo](../Samples~/Demo/README.md) 并用菜单创建场景；下面的代码说明如何接入项目自己的 Prefab。

## 资源加载接口

UI 只认识下面的接口，不引用 YooAsset、Addressables 或游戏的 ResourceManager：

```csharp
public interface IUIAssetLoader
{
    Task<GameObject?> LoadPrefabAsync(string prefabPath);
    void ReleasePrefab(GameObject prefab);
}
```

没有自定义配置时使用 Resources。已有资源库的项目在启动位置安装一次适配器：

```csharp
void ConfigureUi(IUIAssetLoader gameLoader)
{
    UI.UIManager.Instance.SetAssetLoader(gameLoader);
}

// 也可以直接连接项目的两个函数，不额外编写适配类。
void ConfigureUi(
    Func<string, Task<GameObject?>> acquirePrefab,
    Action<GameObject> releasePrefab)
{
    UI.UIManager.Instance.SetAssetLoader(
        new UI.DelegateUIAssetLoader(acquirePrefab, releasePrefab));
}
```

传入的 key 对 UI 是不透明字符串，可由项目解释为 Resources 路径、资源地址或其他标识。每次成功加载代表一份租约，即使多个调用返回同一个 Prefab 也要分别计数。UI 缓存期间保留租约，实例实际销毁后归还；适配器不能释放其他使用者仍依赖的共享资源。UI 不会 Dispose 项目共享的 loader。

切换 loader 会先关闭旧 UI、废弃旧管理器。迟到的加载仍归还给它原来的 loader，不会交给新 loader。取消只保证结束 UI 等待并清理该次所有权；不支持中断的底层读取仍可能继续，不会伪装成下载或磁盘操作已经被终止。

## 每次打开的任务和订阅

在 UIView 子类中：

```csharp
protected override void OnOpen(object? args)
{
    Track(UI.UIManager.Instance.Bus.SubscribeScoped<CoinsChanged>(OnCoinsChanged));
    _ = RefreshAsync(OpenToken); // 捕获此次打开的令牌
}

async Task RefreshAsync(CancellationToken token)
{
    try
    {
        var data = await gameService.LoadAsync(token); // 项目的业务服务
        token.ThrowIfCancellationRequested();
        Render(data);
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    catch (Exception error) { Debug.LogException(error); }
}
```

`gameService/CoinsChanged/Render` 是项目业务接口的示意。可直接编译的简化实现见 Samples 的 `ScopedPageExample`。

- OpenToken：关闭、重置、销毁或同一活动单例再次 OnOpen 刷新时取消；缓存重开得到新令牌。
- Track：收集本次打开的 IDisposable，关闭时自动清理。不同订阅各自拥有，不会删掉其他页面的注册。
- DestroyToken / TrackUntilDestroyed：实例销毁时清理，适合整个实例共用的长期服务。
- OnShow / OnHide：框架显示、隐藏或恢复页面时通知。隐藏不会关闭页面，OpenToken 与本次订阅保留。

不要在旧任务 await 后重新读取 OpenToken 来判断它是否还有效，因为属性可能已经指向下一次打开。Prefab 及其共享依赖由资源加载接口/工厂管理，不要用 Track 在关闭进入缓存时提前卸载这些资源。

缓存仍会保留自定义视图字段。大型业务模型、图片引用或在 OnOpen 中设置的闭包，应由业务在 OnClose 中清理；生命周期作用域管理的是已登记订阅与取消任务，不会自动擦除所有业务字段。

## 取消一次打开

```csharp
using var cancel = new CancellationTokenSource();
var opening = UI.UIManager.Instance.Pages.PushAsync(
    "UI/Pages/Inventory", cancellationToken: cancel.Token);
// 离开该业务流程时 cancel.Cancel();
var handle = await opening;
```

Pages 的 Push/Replace/Reset、Popups.Open、Panels.Show/Toggle/PushInGroup 和 Overlays.ShowBlocking 都有末尾可选 `cancellationToken`。调用方取消会抛 OCE，不会进入导航栈或生成迟到的可见页面。多个 Overlay 使用者各有自己的取消与 token，取消一个不会结束其他使用者的状态层。

## 关闭控制与返回

忙碌期间的立即拦截可覆盖 `protected virtual bool CanClose(UICloseReason reason)`，例如保存中返回 false。它应当是状态判断；需要弹出确认时设置 CloseGuard：

```csharp
protected override void OnCreate() => CloseGuard = ConfirmCloseAsync;

async Task<bool> ConfirmCloseAsync(UICloseReason reason, CancellationToken token)
{
    if (!hasUnsavedChanges) return true;
    var choice = await UI.UIManager.Instance.Dialogs.ShowAsync<bool>(
        "UI/Popups/ConfirmPopup", "Discard changes?", cancellationToken: token);
    return choice.HasValue && choice.Value;
}
```

业务主动关闭使用 `Popups.CloseAsync(handle)`、`Pages.PopAsync()`、`Panels.HideAsync(handle)`，得到 bool 表示是否关闭。同步 TryClose/Pop/Hide 不会绕过异步 Guard；已有 HandleBack 同步入口会自动启动确认，HandleBackAsync 可等待处理结束。

顶层拒绝关闭、确认尚未结束或存在阻塞 Overlay 时，返回操作不会穿透关闭下层。重复返回共用正在处理的请求，避免双弹窗。每个等待者的取消独立；全部等待者离开后停止共享确认。返回方法的 bool 表示是否消费该返回操作，不能用它推断窗口一定已关闭。

CloseReason 包括 Programmatic、Back、Blocker、Replaced、Reset、Disposed、Destroyed、Cancelled、Completed。框架 Reset/owner Dispose、打开取消以及 Dialog 显式完成等会强制结束对应所有权，防止清理被业务拦截永久阻塞；正常交互仍遵守关闭策略。

## 等待弹窗结果

具体 Prefab 组件继承 `UIResultPopup<bool>` 等闭合类型，按钮调用 `Complete(true)` 或 `Complete(false)`：

```csharp
var result = await UI.UIManager.Instance.Dialogs.ShowAsync<bool>(
    "UI/Popups/ConfirmPopup", "Delete this item?", cancellationToken: token);
if (result.HasValue && result.Value)
    DeleteItem();
```

HasValue=true 表示明确选择，false 的选择仍是有效结果；返回键、遮罩、程序关闭或销毁返回 HasValue=false 与 CloseReason。取消调用方 token 则抛 OCE。Value 在无值时抛异常，先检查 HasValue，或使用 TryGetValue。

结果完成后先关闭该弹窗再返回业务。Dialog 默认各自实例，不支持 SingletonByPath，避免多个业务共享一个结果。同步 OnOpen 完成结果、加载后立刻关闭及缓存复用均保留本次结果；关闭后组件不继续持有已返回的业务对象。异步按钮/服务回调可以先调用 `CaptureCompletion()` 保存本次会话的完成函数，旧函数不能完成后来复用的新弹窗；也应使用 OpenToken 取消旧工作。

可编译组件见 Samples 的 ConfirmPopup，需要自行创建 Prefab 并绑定按钮/文本。

## 手机安全区

在每个界面中创建 SafeContent，将交互内容放在其下并挂 UISafeArea；全屏背景留在 SafeContent 外。组件按 Screen.safeArea、Canvas/相机 viewport 和父 RectTransform 投影设置 anchors，适配刘海、挖孔、手势区、屏幕尺寸与方向变化。

FitHorizontal / FitVertical 分别控制两个轴。稳定状态不重复写布局，禁用后恢复所管理轴的原始 anchors/offsets。不要让其他 LayoutController 同时驱动这些轴。

当前用于轴对齐的屏幕空间 Canvas 内容容器，不作为 WorldSpace 或任意透视/旋转 UI 的投影适配器。真实 CanvasScaler、设备旋转和安全区应在目标设备验证。

## 固定尺寸虚拟列表

```csharp
list.Configure(scrollRect, itemPrefab, itemExtent: 64);
list.SetData(items,
    (row, item, index) => row.GetComponent<ItemCell>().Bind(item),
    row => row.GetComponent<ItemCell>().Unbind());
list.ScrollTo(100);
list.RefreshItems();
list.Clear();
```

支持水平或垂直单列；行尺寸固定。ScrollRect.Content 直接位于 Viewport 下，Viewport 使用 Mask/RectMask2D 裁剪；Content 不挂 LayoutGroup/ContentSizeFitter。项目将已加载的行 Prefab 传入，组件不认识具体资源库。默认 overscan=1，maxVisibleItems=512 是误配置保护，超过时明确报错，避免静默缺行。

条目数量只影响 Content 长度，可见窗口内实例环形复用；Bind 必须重置所有行状态，Unbind 用于取消旧图标请求或解除旧监听。SetData 持有原列表引用，不复制数据；结构变化后重新 SetData，同数量内容变化可 RefreshItems。组件不处理动态高度或网格。

Prefab 租约由项目持有，直到列表停止使用且所有行真正销毁。Clear/重新 Configure/OnDestroy 只是提交 Destroy，不能在调用后立刻卸载 Prefab。将行 Prefab 作为界面 Prefab 的序列化依赖时，可随外层界面的资源租约一起管理。

业务绑定中的字符串格式化、图标读取等仍可能分配；虚拟列表的稳定窗口复用与这些业务成本分别测量。


## 固定尺寸虚拟网格

UIVirtualGrid 提供固定列数的垂直网格，复用 UIVirtualList 的行窗口；行内复用项目传入的 Cell Prefab。

```csharp
grid.Configure(scrollRect, cellPrefab, new Vector2(100, 80), columnCount: 4);
grid.SetData(items, (cell, item, index) => cell.GetComponent<ItemView>().Bind(item),
    cell => cell.GetComponent<ItemView>().Unbind());
grid.ScrollTo(9999);
```

每次 SetData/SetItemCount 重置滚动位置；ScrollTo 使用条目索引。RefreshItems 更新同数量内容。最后一行不足列数时隐藏剩余 Cell，maximumVisibleCells 限制可见及 overscan 行的总 Cell 数。列宽和间距由调用方配置，需使其适合 Viewport；不自动换列或处理动态高度。Content 同样不能挂 LayoutGroup/ContentSizeFitter。Prefab 所有权和原生销毁等待与虚拟列表一致。回收前调用 Unbind 清理旧异步请求和监听。
