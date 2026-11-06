# Framework HybridCLR

独立 Unity package：`com.novorizon.framework.hybridclr` 0.1.0-preview.11。运行时与构建工具都包含在本包，不再要求项目复制 Demo 的中间层。Unity 最低版本 2022.3，当前核对版本 2022.3.62f1；Framework 0.15.0-preview.1，官方 HybridCLR 9.0.3。

## 安装

通过聚合安装包或 GameStarter 的模块窗口安装本封装。`package.json` 声明 Framework 与官方 HybridCLR 9.0.3 的版本依赖；官方包由 UPM 从上游获取，不随本仓库、封装包或聚合 tarball 分发。

直接配置使用方工程清单时，先提供官方 Git 来源 `https://github.com/focus-creative-games/hybridclr_unity.git#6cadd24d101f4cbd00cd80190106f6621132d24d` 及匹配 Framework。Unity 不支持 package.json 中的 Git 传递依赖，因此来源由使用方 manifest.json 或安装入口提供。

9.0.3 官方原生安装器依赖物理 Packages 路径，安装入口在使用方工程通过 UPM Embed 完成兼容，再执行本包 Configure；源码不会纳入我们维护的包中。

## 三个入口

- **1. Setup Project**：生成独立热更新 asmdef 和最小 HotGame 入口，不生成启动 JSON。已存在的源码保留；不创建或替换项目场景。
- **2. Build New Player**：当前目标设为 Release IL2CPP；自动发现配置目录中的 Player asmdef，通过依赖排序后写入 HybridCLR，安装匹配原生运行时，生成桥接和裁剪信息，分析 AOT，冻结基线并构建。需要项目已将首场景加入 Build Settings。
- **3. Export Patch**：只编译新热更新代码，以原主包的冻结 AOT 检查缺失 API、元数据集合、桥接与输入指纹。自动递增当前基线的导出版本；CI 可设置 HOTUPDATE_VERSION 指定更高版本。

支持 Windows x64、Android ARM64、iOS ARM64。原生工具链与平台模块由 Unity 安装环境提供。每次 Build New Player 都创建新的基线；Advanced 菜单提供分别准备和构建已准备主包的入口。

运行时由项目的 GameEntry 显式启动。基础 Manager 初始化完成后调用：

```csharp
await HotUpdateManager.Instance.InitializeAsync(new UpdateSettings(), token);
```

退出时先调用 `HotUpdateManager.Release()`，再释放其他 Manager。它复用 Framework 的 ConfigurableSingleton，是普通 C# 服务，不创建 GameObject，不使用 RuntimeInitializeOnLoadMethod 自动启动，也不读取启动 JSON。重复初始化返回同一个 Task；失败向调用方报告，入口对象的取消与清理由包内部处理。

默认配置使用 Unity 的 Application.identifier，热更目录为 Assets/HotUpdate，程序集为 Game.HotUpdate，入口类型为 Game.HotUpdate.HotGame。自定义参数直接通过 UpdateSettings 传入。构建菜单默认使用同样的配置；需要自定义时，在项目 Editor 初始化代码中将 `GameBuild.SettingsFactory` 绑定到项目共享的 C# 配置工厂，运行时也调用这个工厂：

```csharp
// 放在项目的 AOT 程序集中。
public static class ProjectHotUpdate
{
    public static UpdateSettings CreateSettings() => new UpdateSettings
    {
        channel = "development"
    };
}

// 放在项目的 Editor 程序集中，并引用 Game.Framework.HotUpdate.Editor。
[UnityEditor.InitializeOnLoad]
public static class ProjectHotUpdateBuild
{
    static ProjectHotUpdateBuild()
    {
        Game.Framework.HotUpdate.Editor.GameBuild.SettingsFactory = ProjectHotUpdate.CreateSettings;
    }
}
```

Editor 使用 Unity 已编译的程序集；Player 准备版本、补充 AOT 元数据并加载热更 DLL 后创建入口对象。热更初始化完成后 GameEntry 才进入 Ready；HotGame 不反向等待 GameEntry，避免循环等待。停用热更时，项目直接跳过 InitializeAsync。

热更新目录只收集实际参与 Player 编译的 asmdef，支持名字/GUID 引用和多个程序集；AOT 程序集不能直接引用热更程序集。共享契约放在热更目录之外。新增或改变热更新程序集依赖顺序需要新主包。未执行 Setup Project，或未建立热更目录与 asmdef 时，本包不会拦截普通 Unity 构建。

## 项目入口

```csharp
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.HotUpdate;

public sealed class HotGame : IGameModule
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

## HotGame 的创建与调用

HotGame.cs 定义类型；加载完成后，HotUpdateManager 通过 Activator.CreateInstance 创建运行时对象，再调用 IGameModule.InitializeAsync。HotGame 是普通 C# 对象，不显示在 Hierarchy。

GameStarter 的场景中只需要 GameEntry。它依次准备基础 Manager、可选启动模块和热更入口，负责整个启动顺序。GameEntry 的 Initialize Hot Update 开关默认开启；关闭后不会创建或加载热更入口。加载完成之前不要打开依赖热更脚本的场景。

默认类型是 Game.HotUpdate.HotGame，程序集仍为 Game.HotUpdate；IGameModule 契约保持不变。升级现有项目时，移除旧的 GameBootstrap/HotGameBootstrap 场景组件和 NovorizonHotUpdateSettings.json，在自己的入口显式调用 InitializeAsync，并将配置移到 C#。Setup Project 不覆盖项目已有的自定义入口源码。此次 AOT 启动流程与入口类型变化需要重新构建 HybridCLR 主包。

Editor 程序集直接引用 Game.Framework.Core，以访问 HotUpdateManager 继承的 ConfigurableSingleton API。编辑器启动检查用 TryGetInstance 读取已有实例，不负责创建或启动热更；GameEntry 使用 Instance.InitializeAsync 显式启动。验证器按 Unity asmdef 将 Runtime 与 Editor 分开编译，并关闭传递项目引用。


项目可以使用 InitializeAsync(settings, startupContext, token) 传入自己的启动参数。热更端通过可选 IGameContextHost.StartupContext 取得对象；IGameHost 和 IGameModule 原有方法保持不变，原有模块仍可使用旧重载。参数不被封装序列化，不进入发布清单。GameStarter 使用独立 Game.Startup AOT 程序集定义具体参数类型，热更端可以读取但不反向等待 GameEntry 就绪。新增 AOT 接口和项目契约程序集需要重建 Player 基线。

## 配套资源版本

ReleaseManifest.content 可绑定 packageName/version，签名覆盖该绑定。UpdateSettings.prepareContentAsync 在读取并验证代码后、装载 AOT 元数据或程序集之前准备对应资源；失败时只在装载代码前回到配套内置发行。scopeRemoteByBaseline 可按 platform/channel/baselineId 选择清单；remoteFilesBaseUrl 可将 DLL 与清单部署到不同 HTTPS 前缀。

loadFromCompiledAssemblies 用于本地编译的 IGameModule；Editor 自动使用编译源。IGameApplicationEvents 是可选的暂停/焦点通知，由宿主通过 NotifyApplicationPause/Focus 转发。

Editor 的 SettingsFactory 可提供当前 ReleaseContent；资源版本不计入固定 AOT 输入指纹，程序集拓扑、AOT 元数据与其他固定设置仍被校验。ExportUpdateToDirectory 返回未签名输出目录，方便项目接入既有签名发布流程。已有主包需要重建。
