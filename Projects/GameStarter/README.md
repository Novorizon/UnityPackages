# GameStarter

Unity 2022.3.62f1 的基础启动工程，默认只依赖同仓库的 Framework 包。通过 Unity Hub 打开本目录。

打开 `Assets/Scenes/Bootstrap.unity` 后 Play，空入口输出 `GAME_STARTER_READY`。通过 **Tools/Novorizon/Optional Modules** 安装 UI、HybridCLR、Ability、RTree、GpuAnimator、ECS 或 YooAsset 适配。安装来源为根目录 `Packages/` 的独立包，整个仓库复制后相对地址仍有效。

需要完整集合时，通过 Package Manager 的 Add package from disk 选择根目录 `Packages/com.novorizon.framework.all/package.json`，等待聚合安装及 HybridCLR 默认配置完成。安装前等待其他 UPM 操作结束。

GameEntry 继承 Framework MonoSingleton。项目自己的组件继承 StartupModule，放在 GameEntry 根对象并填入 startupModules；按顺序异步初始化，全部成功才更新，失败或关闭时逆序清理。异步模块需要观察取消并支持部分初始化后清理。

HybridCLR 的 `Game.Framework.HotUpdate.IGameModule` 是热更入口契约；StartupModule 用于项目 AOT 启动服务，默认列表为空。共享契约放在 AOT 程序集，热更代码单向引用。

```sh
python3 Tools~/verify.py --dotnet /path/to/dotnet
```

验证自动从同仓库读取真实 Framework Core 和 MonoSingleton 源码，Unity/UPM 使用模型。没有在此环境执行真实 Unity、IL2CPP 或设备验证。完整仓库导出使用根目录 `tools/workspace.py export`，复制工程时保留仓库目录结构。
