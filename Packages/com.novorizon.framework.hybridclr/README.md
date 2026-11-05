# Framework HybridCLR

独立 Unity package：`com.novorizon.framework.hybridclr` 0.1.0-preview.3。运行时与构建工具都包含在本包，不再要求项目复制 Demo 的中间层。Unity 最低版本 2022.3，当前核对版本 2022.3.62f1；Framework 0.10.0-preview.2，官方 HybridCLR 9.0.3。

## 安装

通过聚合安装包或 GameStarter 的模块窗口安装本封装。`package.json` 声明 Framework 与官方 HybridCLR 9.0.3 的版本依赖；官方包由 UPM 从上游获取，不随本仓库、封装包或聚合 tarball 分发。

直接配置使用方工程清单时，先提供官方 Git 来源 `https://github.com/focus-creative-games/hybridclr_unity.git#6cadd24d101f4cbd00cd80190106f6621132d24d` 及匹配 Framework。Unity 不支持 package.json 中的 Git 传递依赖，因此来源由使用方 manifest.json 或安装入口提供。

9.0.3 官方原生安装器依赖物理 Packages 路径，安装入口在使用方工程通过 UPM Embed 完成兼容，再执行本包 Configure；源码不会纳入我们维护的包中。

## 三个入口

- **1. Setup Project**：生成设置、独立热更新 asmdef 和最小 IGameModule 入口。已存在的源码与设置保留；不创建或替换项目场景。
- **2. Build New Player**：当前目标设为 Release IL2CPP；自动发现配置目录中的 Player asmdef，通过依赖排序后写入 HybridCLR，安装匹配原生运行时，生成桥接和裁剪信息，分析 AOT，冻结基线并构建。需要项目已将首场景加入 Build Settings。
- **3. Export Patch**：只编译新热更新代码，以原主包的冻结 AOT 检查缺失 API、元数据集合、桥接与输入指纹。自动递增当前基线的导出版本；CI 可设置 HOTUPDATE_VERSION 指定更高版本。

支持 Windows x64、Android ARM64、iOS ARM64。原生工具链与平台模块由 Unity 安装环境提供。每次 Build New Player 都创建新的基线；Advanced 菜单提供分别准备和构建已准备主包的入口。

配置文件为 `Assets/Resources/NovorizonHotUpdateSettings.json`。修改 `hotUpdateRoot`、`entryAssembly`、`entryType` 可使用项目自己的命名。热更新目录只收集实际参与 Player 编译的 asmdef，支持名字/GUID 引用和多个程序集；不能从 AOT 程序集直接引用热更新程序集。共享契约放在热更新目录之外。新增或改变热更新程序集依赖顺序需要新主包。

自动启动仅在配置存在且 `autoStart=true` 时启用；Editor 执行 Unity 已编译源码，Player 按清单依次加载内置 AOT 元数据和热更新 DLL。关闭 autoStart 后，项目可在主线程访问 `GameBootstrap.Instance` 显式启动。启动对象继承 Framework 的 MonoSingleton，不另实现单例。游戏入口自行取消初始化会报告启动失败并释放模块；关闭或销毁会话引起的取消按正常终止处理。

没有配置时，本包不会创建启动对象，也不会拦截普通 Unity 构建。

## 项目入口

```csharp
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.HotUpdate;

public sealed class GameModule : IGameModule
{
    public async Task InitializeAsync(IGameHost host, CancellationToken token)
    {
        await LoadGameAsync(token); // 项目自己的服务、配置和首场景
        host.ReportReady("1.0.0");
    }
    public void Shutdown() { /* 释放项目资源 */ }
}
```

入口类型需要完整命名空间、公开无参构造，且实现 IGameModule。IGameHost 的启动确认绑定当前会话；旧会话不能确认新启动。热更新程序集引用 `Game.Framework.HotUpdate`；需要其他 Framework 能力时引用对应公开程序集即可。

## 远程更新与签名

默认只运行随包代码。项目启用远程更新时提供 `enableRemote=true`、应用 ID、渠道、以 `/` 结尾的 HTTPS `remoteBaseUrl` 和 `trustedKeys`。地址对应当前平台、渠道、主包基线；不提供示例服务器、账号或业务 API。

包内 `Tools~/ReleaseTool` 是 .NET SDK 8 控制台工具。以本包真实目录替换 `<package>`：

```sh
dotnet run --project <package>/Tools~/ReleaseTool -- keygen /external/signing release-2026
dotnet run --project <package>/Tools~/ReleaseTool -- sign <unsigned-directory> /external/signing/release-2026.private.pem release-2026 <new-publish-directory>
dotnet run --project <package>/Tools~/ReleaseTool -- verify <publish-directory>/release.json /external/signing/release-2026.public.json
```

将生成的公钥 JSON 放入设置的 trustedKeys，再构建主包。先上传签名工具输出的内容哈希子目录，最后原子更新 release.json。私钥留在项目外，只由发布环境持有。工具生成受限文件权限，不会把私钥复制进补丁。

客户端先验 RSA-3072/SHA-256 签名，再检查基线身份、大小和 SHA-256。AOT 元数据只来自主包，不能远程替换。下载分阶段提交，缓存保留有效回退版本；启动失败或进程在健康确认前退出，下次启动隔离失败版本。加载代码后不能在进程内切换版本。运营回退使用更高版本号发布旧内容。

Unity/HybridCLR、AOT 代码、公共契约、入口配置或热更新拓扑变更须重新构建主包；原主包基线及 PLAYER_BUILD_VERIFIED 制品必须长期保存。Android 主包仅接受 ARM64；启动配置也纳入基线指纹。当前输入指纹会保守地拒绝 AOT 源文件、外部引用 DLL、项目设置、清单或 linker XML 的变化。

## 验证边界

仓库包含更新恢复、构建图、配置幂等、启动生命周期及发布工具回归。headless 测试编译真实 Framework 单例、包源码、官方分析器，Unity/native 命令为显式边界；不模拟真实 Editor 导入、IL2CPP、HTTPS、Android/iOS 或热更新后的设备执行。发行前仍需按目标平台完成实际验证。
