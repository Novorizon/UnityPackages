# Resource Demo

使用当前项目的 YooAsset DefaultPackage，演示显式初始化、`AcquireAsync<TextAsset>`、取消和独立租约释放。

1. 安装 Framework、官方 YooAsset 和适配包；为 DefaultPackage 配置一个 TextAsset 采集地址。
2. Package Manager 导入 **Resource Demo**，执行 Tools → Novorizon → Demos → **Create Resource Demo Scene**。
3. Play 后点击 Initialize，填写地址，再点击 Acquire TextAsset 查看预览。
4. Release 只释放此控制器的租约；再次加载会替换它自己的旧租约。

Editor 使用模拟模式，需要采集配置。Player 默认使用平台匹配的内置内容；Host 模式需要项目配置的远端资源。示例不附带 Bundle 或公共测试服务器。

销毁控制器会取消待完成获取并释放自己的租约，不关闭全局 ResourceManager，不调用共享地址的 ReleaseHandle。租约及初始化行为见 [资源说明](../../Documentation~/Resources.md)。
