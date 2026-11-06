# Novorizon Input

独立输入包，复用 Framework 单例，依赖 Unity Input System 1.11.2，支持 Unity 2022.3。键鼠、手柄和触摸绑定由项目自己的 `.inputactions` 定义，包不规定移动、技能等游戏动作。

通过 framework.all 安装，或先安装 Framework，再安装本包。Input System 从 Unity 注册表解析，不随本包复制。项目的 **Player Settings → Active Input Handling** 选择 **Input System Package (New)** 或 **Both**；仍使用旧 UI 输入模块时选择 Both。设置变化需要按 Unity 提示重启编辑器。GameStarter 已设为 Both。

```csharp
using Game.Framework.Input;
using UnityEngine.InputSystem;

var input = InputManager.Instance;
input.Initialize(projectInputActions, "Gameplay");
var move = input.FindAction("Gameplay/Move");
var direction = move.ReadValue<UnityEngine.Vector2>();
var subscription = input.Subscribe("Gameplay/Confirm", context => Confirm());
// 窗口或模块退出时解除订阅。
subscription.Dispose();
```

Initialize 克隆 Action Asset，不修改项目资源；传入 null 时创建空资产。启用状态由管理器维护：`SwitchMap` 独占切换，`EnableMap`/`DisableMap` 支持 UI 和 Gameplay 并存。`Actions`/`FindAction` 用于读取原生动作；启停使用管理器接口，直接调用原生 Enable 会绕过屏蔽规则。

```csharp
// 暂时屏蔽 Gameplay；多个窗口可各自持有嵌套租约。
using (input.Block("Gameplay"))
{
    await ShowModalAsync();
}
input.SetControlScheme("Gamepad"); // 按 binding group 过滤
input.SetDevices(myGamepad);      // 可选的设备过滤
input.SetDevices(null);           // 恢复全部设备
```

控制方案切换只管理此副本的 binding mask，不接管 PlayerInput 的多人设备配对；项目使用多人 PlayerInput 时可直接使用官方组件。

```csharp
// bindingIndex 选择普通绑定或复合绑定的一部分，不能选择整个复合绑定。
string json = await input.RebindAsync("Gameplay/Confirm", bindingIndex, cancellationToken);
// Escape 或 CancellationToken 取消会抛出 OperationCanceledException。
LocalStorageManager.Instance.SaveText("settings/input-bindings.json", json);
input.LoadBindingOverrides(json); // 下次启动读取存储后恢复
```

重绑定期间关闭对应动作，结束后遵守当前 Map/屏蔽状态恢复。每个管理器一次只能运行一个重绑定；销毁时取消未完成操作、解除订阅、销毁运行时副本，旧屏蔽租约不会操作新实例。`LocalStorageManager` 位于 `Game.Framework.Storage`，上例保存操作由项目调用，输入包不负责持久化。

所有控制、Dispose 和订阅释放在 Unity 主线程调用；异步重绑定使用 Unity SynchronizationContext。GameStarter 在 GameEntry 中初始化和清理，普通项目退出时调用 Dispose/Reset。设备事件、触摸、原生重绑定交互仍需在 Unity 与目标设备验收。
