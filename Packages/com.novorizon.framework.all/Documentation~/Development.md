# 开发、打包与验收

版本锁定文件是 `Dependencies.json`。基础库继续使用 Framework 名称，聚合安装器只有独立 Editor 程序集，不引用 Framework 或可选模块程序集。

## 源码验证

从 UnityPackages 仓库运行，Python 3.12+、.NET SDK 8+：

```sh
python3 tools/workspace.py verify --dotnet /path/to/dotnet
python3 tools/workspace.py sync
python3 tools/workspace.py export --output /tmp/UnityPackages-release
```

默认使用 `dotnet`。验证脚本从临时目录调用 SDK，单独编译本安装器的 Editor 代码；测试 Unity/UPM API 契约、请求生命周期、丢失句柄后的恢复、错误和复制流程。测试模型不会执行真实 Unity 导入或重编译。

打包器只打包本包。工作区工具从各独立包目录生成与版本锁定 SHA-256 匹配的归档，验证全部 `package.json` 名称、版本和自有依赖闭包，附带原样归档作为 Payload~。输出采用固定时间戳及文件顺序，附带 SHA-256 校验文件。不修改各模块源码或原第三方许可，Odin 商业插件不分发。

官方 YooAsset 2.3.18 归档取自 `tuyoogame/YooAsset` 提交 `4b6a8ca4064d7a59ba129acec8fecf134e86236e` 的 `Assets/YooAsset`，保留全部 Samples~、许可证与 importer meta。官方 HybridCLR 9.0.3 使用现有验证归档，对应提交 `6cadd24d101f4cbd00cd80190106f6621132d24d`。RTree 继续遵循 LGPL-2.1-or-later。

## 原生 Unity 验收（未执行）

1. 在 Unity 2022.3 的干净 Built-in 工程分别测试 tarball 版与 Git 版：只添加聚合包一次，等待完成日志；检查 11 个锁定包均安装。
2. 核对 tarball 版生成的 `Packages/manifest.json` 使用项目相对归档地址，复制项目（包含归档目录）到其他位置后重新打开。
3. 确认 HybridCLR 源是 Embedded、Data~ 和插件 meta 保留，默认 Resources 设置、热更 asmdef 与入口生成；已有项目设置不被覆盖。
4. 在依赖安装、嵌入或配置期间触发重编译/重启 Editor，确认不会并发提交 UPM 操作、漏装或重复配置。
5. 检查已有同版本、冲突的嵌入版本、网络中断、Payload 缺失或损坏的结果；重试应保留已安装模块并完成后续流程。
6. 按各包的原生测试文档执行 UI、ECS、Ability、空间查询、GPU 烘焙/播放/回收与资源加载验收，完成一次真实 HybridCLR IL2CPP Player 构建和补丁加载。

可在已安装本包的隔离验收工程中运行：

```sh
Unity -batchmode -nographics -projectPath /path/to/test-project -executeMethod Game.Framework.All.Editor.AllPackagesInstaller.InstallBatch -logFile /tmp/novorizon-all-unity.log
```

UPM 运行期间不能带 `-quit`。入口在失败时以非零退出；完成时再次核对注册包版本、Embedded HybridCLR 和 Resources 设置，并写入 `Library/NovorizonFrameworkAll/verification.json` 后退出 0。CI 应同时检查进程退出码及本次报告；该报告只证明实际 Editor 导入和配置，不证明 IL2CPP 构建、GPU、网络或设备运行。
