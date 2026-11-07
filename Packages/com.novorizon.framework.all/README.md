# Novorizon Framework All

安装一个包，由 UPM 自动解析 Framework、UI、Ability（含编辑器）、ECS、RTree、GpuAnimator/URP、多语言、诊断、YooAsset 适配和 HybridCLR 封装及其依赖。

Package Manager → Add package from git URL：

```text
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework.all#v0.1.0-preview.33
```

也可安装导出的单个 `com.novorizon.framework.all-<package.json 中的 version>.tgz`。聚合包只包含 Editor 安装器与依赖目录，不捆绑其他包源码、tarball 或 Payload~。安装需要 Git 和正常网络访问。

## 依赖声明

各包的 `package.json` 声明版本关系，例如 YooAsset 适配依赖官方 `com.tuyoogame.yooasset` 2.3.18，HybridCLR 封装依赖官方 `com.code-philosophy.hybridclr` 9.0.3。上游包的源码不在本仓库维护。

Unity 不支持 package.json 中的 Git 传递依赖。当前 Git 分发由本 Editor 安装入口通过一次 Client.AddAndRemove 提交固定来源，之后 UPM 负责下载、解析与缓存。自有库来自本仓库；第三方来自各自官方仓库。旧包名由依赖目录中的迁移清单统一替换。

## HybridCLR

安装完成后调用封装 Configure 生成默认设置与入口。官方 9.0.3 的安装器依赖物理 Packages 路径，因此在使用方工程执行 UPM Embed；官方源码不随本仓库或聚合 tarball 分发。首次 Player 构建和 DLL/AOT 收集由封装菜单处理。

Odin Inspector 插件由项目自行提供；缺少 Odin 不影响 Ability 运行时。默认使用 Unity 2022.3.62f1 / URP 14.0.12；没有现有 Graphics/当前 Quality 管线时创建默认 URP 资产，已有配置保留。GPU 烘焙默认 URP Unlit，Built-in 可显式选择。

安装进度保存在使用方 ProjectSettings/NovorizonFrameworkAll.install.json，跨重编译和重启恢复。失败后先修复 Console/UPM 错误，再使用 Tools/Novorizon/All Packages/Retry Installation。已经安装的包保留。

状态文件 JSON 损坏时，自动安装停止并保留文件内容；Retry Installation 会重新建立状态。重编译丢失 UPM 请求句柄后，安装器每半秒检查一次注册结果，避免每个 Editor update 都创建包列表快照。

## 验证

22 组安装 API 模型验证覆盖批量依赖、请求恢复、失败重试、损坏状态恢复、注册查询频率、旧包迁移及第三方必须使用上游来源。没有执行真实 Unity/UPM/IL2CPP 或设备验收。维护说明见 [Development.md](Documentation~/Development.md)。
