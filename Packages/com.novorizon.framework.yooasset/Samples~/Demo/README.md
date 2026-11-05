# Resource Demo

这个可选示例检查当前项目的 YooAsset 2.3.18 `DefaultPackage`、TextAsset 加载和共享缓存。它不会在导入或进入 Play 时自动初始化、联网或修改资源配置。

## 准备

1. 安装主框架、官方 YooAsset 2.3.18 和本可选适配包。
2. 在项目建立 DefaultPackage 采集配置，采集一个可读取的 TextAsset，并记下它的 location。示例不附带采集配置或 AssetBundle。
3. 在 Package Manager 的本包 Samples 中导入 **Resource Demo**。
4. 点击 **Tools > Novorizon > Demos > Create Resource Demo Scene**。菜单先询问是否保存当前修改，再创建 `Assets/NovorizonDemos/ResourceDemo*.unity`，使用唯一文件名，不覆盖已有场景。
5. 进入 Play，在仪表盘显式操作。也可在独立空场景创建 GameObject，挂 `ResourceDemoController` 后运行。

Editor 使用模拟构建，需要项目的 DefaultPackage collectors。Player 默认 Offline，需要平台匹配的内置内容；定义 HOST_PLAY_MODE 后，点击初始化可能访问项目配置的远端地址。示例不提供可直接连接的 URL。

## 按钮与预期

| 操作 | 预期 |
| --- | --- |
| Initialize DefaultPackage | 成功显示 Ready；缺配置则显示失败信息，结合 Console 排查 |
| 输入 location，Load TextAsset | 显示最多 600 字符预览；再次加载使用共享缓存 |
| Query completed cache | 已完成缓存显示 hit，否则 miss；不触发加载 |
| 勾选共享释放确认，再 Release | 只释放本控制器最近成功加载的位置；再次 Query 应 miss（其他代码可能重新加载） |

location 是 YooAsset 配置中的地址，不一定是磁盘路径。TextAsset 不匹配的地址会显示类型错误。未初始化、空输入和未确认的释放操作都有可见提示。

## 清理边界

ResourceManager 的缓存按 location 共享，没有“每次 Load 自动多一份租约”。释放测试地址会影响这个地址的所有消费者，因此只在独立 Demo 场景使用、不与业务共享同一地址，并在释放前勾选确认。

Demo 退出时停止更新自己的预览，不执行全局 Shutdown、ReleaseAllAssets，也不擅自释放在途请求返回的共享资源。重复测试多个地址可能保留多个缓存项，由资源包/应用所有者确认其他消费者结束后统一管理；需要释放之前的位置时可再次加载该 key，再显式 Release。UI 或对象池仍持有依赖时不得卸载。

需要不依赖 YooAsset 的演示时，使用主框架 Core Demo 或 UI Demo；UI Demo 通过 `IUIAssetLoader` 的内存模板实现运行，不需要本包。

## 验证范围

样例 Runtime/Editor API 可以由仓库合同项目编译；它不证明真实 Bundle、模拟构建、远端访问、渲染或 IL2CPP 可运行。请在 Unity 2022.3 + YooAsset 2.3.18 项目确认菜单、采集、加载、释放与目标 Player。
