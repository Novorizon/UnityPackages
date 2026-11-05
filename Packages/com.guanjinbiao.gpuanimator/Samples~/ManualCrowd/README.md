# 群体：共享数据与统一 Tick

此示例只包含脚本，不附带模型或烘焙资产。准备同一次烘焙生成的 prefab 及 `.bytes`；prefab 根节点需要有 Renderer，并已绑定对应的网格、动画纹理和材质。

## 导入与配置

1. 在 Unity Package Manager 中选择 **Gpu Animator**，导入 **Manual Crowd** 样例。
2. 在场景中创建空对象，添加 `GpuAnimatorCrowdExample`。
3. 将自己的烘焙 prefab 拖入 **Baked Prefab**，将配套 `.bytes` 拖入 **Animation Data Asset**。
4. **Clip Name** 填写数据中真实存在的完整动画名称，大小写一致。脚本没有预设动画名。
5. 设置 **Count**（1..1000）、**Columns**（至少 1）和 **Spacing**（正数，单位为本地坐标的 Unity 单位）。

## 操作

进入 Play Mode，在组件菜单中执行 **Spawn Crowd**。脚本会把实例放在该空对象下，沿本地 X/Z 方向排成网格。

- 动画 `.bytes` 对当前 TextAsset 仅解码一次，所有实例共享同一个 `AnimationData`。
- 每个实例只在创建时查找 Renderer 和播放器。播放器设为 `AutomaticUpdate = false`，由群体管理脚本的 `Update` 遍历缓存列表调用 `Tick(Time.deltaTime)`，避免自动和手动重复推进。
- `TryPlay` 使用 Inspector 提供的名称，并显式传入 `loop: true` 与 `randomizeStart: true`。随机偏移沿用播放器已有的动画首段规则，并非整个周期均匀随机。
- 再次执行 **Spawn Crowd** 会先清理旧实例。执行 **Clear Crowd** 或销毁管理对象会清理它创建的播放器和对象。生成过程中发生错误时，也会清理已生成实例。

控制方法是公开无参方法，可以由自己的业务代码调用。组件菜单只允许在 Play Mode 中执行，示例不会在编辑状态创建或删除场景对象。

## 性能与业务接入

样例演示的是传统 GameObject 下的数据共享和统一时间驱动。相同 prefab 实例可以共享材质、网格和动画纹理；脚本不访问 `renderer.material`，也不通过 PropertyBlock 绑定纹理。实际是否合批还取决于材质启用 GPU Instancing、Shader、渲染管线和平台，请通过 Unity Frame Debugger/Profiler 检查。

替换为自己的对象池时，可保留同样的初始化和 `Tick` 流程，并在归还对象时调用 `Clear`；本样例为了便于阅读，使用 Instantiate/Destroy。它不包含资源加载器、裁剪系统或对象池。
