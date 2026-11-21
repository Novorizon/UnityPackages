# Resource Demo

使用当前项目的 YooAsset DefaultPackage，演示显式初始化、`LoadAssetAsync<TextAsset>`、取消和 `Release(asset)`。

1. 安装 Framework、官方 YooAsset 和适配包；为 DefaultPackage 配置一个 TextAsset 采集地址。
2. Package Manager 导入 **Resource Demo**，执行 Tools → Novorizon → Demos → **Create Resource Demo Scene**。
3. Play 后点击 Initialize，填写地址，再点击 Load TextAsset 查看预览。
4. Release 归还此控制器的一次引用。再次加载会先归还控件之前的引用。

示例通过 `InitializeAsync(ResourceMode, token)` 初始化。Editor 使用 `ResourceMode.EditorSimulation`，需要采集配置。Player 使用 `ResourceMode.Offline`，需要平台匹配的内置内容。

销毁控制器会取消待完成获取并归还自己的资源引用。资源与初始化行为见 [资源说明](../../Documentation~/Resources.md)。
