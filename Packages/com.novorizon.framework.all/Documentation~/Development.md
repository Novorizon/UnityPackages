# 聚合安装器维护

`Dependencies.json` 由仓库根 `workspace.json`、各自有包 package.json 生成。第三方在 externalPackages 中登记官方版本和 Git 来源，不下载或复制其源码进仓库。

```sh
python3 tools/workspace.py sync
python3 tools/workspace.py verify --dotnet /path/to/dotnet
python3 tools/workspace.py export --output /tmp/UnityPackages-release
```

导出工具分别生成 9 个自有包的 tarball。聚合 tarball 仅包含安装器和依赖元数据，没有其他包归档，也没有 Payload~。源码 ZIP 同样只含本仓库维护的自有包。

实际 UPM 安装时第三方由官方 Git 源解析；HybridCLR 9.0.3 在使用方工程 Embed 后配置，以兼容其物理 Packages 路径需求。验证器的 Unity/UPM 为模型，真实导入、重编译、原生安装及 IL2CPP 需要在仓库外的 Unity 工程验收。

```sh
python3 tools/workspace.py unity-project --output /tmp/NovorizonPackageValidation
Unity -batchmode -nographics -projectPath /tmp/NovorizonPackageValidation -executeMethod Game.Framework.All.Editor.AllPackagesInstaller.InstallBatch -logFile /tmp/novorizon-all.log
```

UPM 运行期间不带 -quit；入口核对版本、HybridCLR Embed 与默认设置后写验证报告并退出。
