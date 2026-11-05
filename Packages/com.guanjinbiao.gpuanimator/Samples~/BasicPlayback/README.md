# 单角色：循环、单次动作与完成回调

此示例只包含脚本，不包含模型或烘焙结果。请先准备同一次烘焙生成的 prefab、动画 `.bytes`、网格、材质和动画纹理。材质与纹理应已绑定在 prefab 的 Renderer 上。

## 导入与配置

1. 在 Unity Package Manager 中选择 **Gpu Animator**，导入 **Basic Playback** 样例。
2. 把自己的烘焙 prefab 放入场景，在该实例上添加 `GpuAnimatorBasicExample`。
3. 将该实例负责 GPU 动画的 Renderer 拖入 **Target Renderer**，将配套 `.bytes` 拖入 **Animation Data Asset**。
4. 在 **Loop Clip Name** 与 **Action Clip Name** 中填写该数据里真实存在的动画名称，大小写和完整名称必须一致。脚本没有预设动画名。

## 操作

进入 Play Mode，通过组件菜单依次执行：

- **Initialize Sample**：解码数据并初始化。相同 TextAsset 重复初始化时复用已解码数据，不自动播放动画。
- **Play Loop**：从指定循环动画的起始帧播放，显式传入 `loop: true`。
- **Play Action**：显式传入 `loop: false`，从单次动作的起始帧播放；完成后通过回调重新播放配置的循环动画。
- **Stop Playback**：停止并保留当前姿势，不调用完成回调。
- **Clear Playback**：停止并释放播放器持有的资源引用；再次播放前需要执行 **Initialize Sample**。示例保留解码缓存以供复用。

示例使用 `AutomaticUpdate = true`，由播放器自己的 `Update` 推进时间，无须手动调用 `Tick`。控制方法均为公开无参方法，也可由业务按钮或自己的代码调用。

## 接入自己的业务

`GpuAnimatorBasicExample` 将 Inspector 引用作为资源注入来源：调用 `AnimationData.FromBytes` 一次，再传给 `Initialize(renderer, data)`。替换成自己的资源系统时，在加载完成后执行同样的初始化即可，无须修改播放器。

示例在指定 Renderer 所在对象上复用或添加 `GpuAnimator`。重新初始化和示例销毁时，只清理它持有的这个播放器；不要让多个控制脚本同时驱动同一播放器。动画缺失或解码失败会输出 Console 错误，不会自动改用其他名称。

模型、网格、动画纹理、材质与 `.bytes` 必须配套；接口无法判断不同烘焙结果是否来自同一模型。示例不会加载资源、克隆材质或通过 PropertyBlock 绑定纹理。
