# Novorizon Data

独立 Luban 数据表包，依赖 Framework 与官方 Luban Unity 运行时 1.2.0。提供 Excel 生成、二进制加载和完整 Tables 快照查询；不管理玩家属性等动态状态，不提供通用 JSON 表容器。

## 加载与查询

Luban 不需要注册。生成的 `cfg.Tables` 已经知道有哪些表、如何读取和解析表间引用；加载时直接传入其构造方式即可：

```csharp
using Game.Framework.Data;

DataManager.Instance.Initialize(); // GameStarter 已初始化时可省略
var tables = await DataManager.Instance.LoadAsync(reader => new cfg.Tables(reader));
var row = tables.TbExample[1];
var current = DataManager.Instance.Get<cfg.Tables>();
```

`LoadAsync` 异步读取清单中的所有二进制文件，再调用生成的同步 Tables 构造函数。不需要 Register，也无需逐张登记表。每种 Tables 类型保存一份当前快照；Get 在尚未加载时抛出异常，也可用 TryGet 查询。表名和行查询 API 由 Luban 生成，框架不重复包装成 Player、Item 等业务属性。

完整构建成功后才替换旧 Tables；加载或构造失败保留上一份有效表，同类型后发请求优先。取消、Clear、Dispose 或过期请求不能发布旧结果。每次生成代码读取文件时使用独立 ByteBuf 游标。Clear 清除快照并使未完成加载失效，Dispose 还会取消本管理器的等待。调用及查询应在 Unity 主线程进行。

## 资源来源

默认通过 Resources.LoadAsync<TextAsset> 读取 `.bytes`，清单和表文件共用一个字节加载器。默认清单地址为 `Config/Luban/manifest`，Resources 地址不含扩展名；需要其他位置时使用 LoadAsync 的 manifestLocation 参数。

GameStarter 开启 YooAsset 后，已接入读取并释放独立资源租约的加载器：

```csharp
DataManager.Instance.Initialize(async (location, token) =>
{
    using (var asset = await ResourceManager.Instance.AcquireAsync<TextAsset>(location, token))
    {
        return asset.Asset.bytes;
    }
});
```

上述资源示例需引用 YooAsset 适配程序集和 UnityEngine。Data 包自身不依赖 YooAsset。一个实例只初始化一次加载来源，重复相同初始化不会重建表；切换来源需要先 Release。自定义加载器必须返回完整字节内容；清单按 UTF-8 JSON 解析，业务数据表使用 Luban bin 格式。

## 从 Excel 生成

通过 framework.all 自动解析官方 `com.code-philosophy.luban`；单独 Git 安装时，也在工程 manifest 声明官方来源：

```json
"com.code-philosophy.luban": "https://github.com/focus-creative-games/luban_unity.git#061314200e2f03307c4d90b448e1de7b85431905"
```

1. 安装 Git 和 **.NET 8 SDK**，重启 Unity/Hub，让编辑器进程能找到 git、dotnet。
2. 使用 **Novorizon → Data → Luban → Create Project**。项目根目录生成 `Config/Luban`，其中有 luban.conf、XML 定义和可编辑的 Data/example.xlsx；Settings 资产保存在 Assets/Settings/LubanGenerator.asset。
3. 修改 Excel 后使用 **Generate**。首次取得固定版本官方 Luban 4.12.0 并编译，只缓存到当前项目 Library/Novorizon/Luban。也可在 Settings 指定已有 Luban.dll 和 dotnet 路径。
4. 默认输出 C# 到 Assets/Generated/Config，`.bytes` 和 manifest.json 到 Assets/Resources/Config/Luban。生成程序集默认为 `Game.Data.Generated`，引用 `Luban.Runtime`；已有 Settings 可保留原生成程序集名。业务 asmdef 需要直接引用生成程序集、Luban.Runtime 和 Game.Framework.Data。

自定义资源采集规则应使地址与清单中的 location 一致，也可修改 Settings.locationPrefix。官方运行时和 CLI 源码不进入本包。

输出目录完全由生成器管理，不能混放手写文件。首次只能写入空目录；后续移除退役输出并保留仍存在文件及目录的 Unity .meta。CLI 失败保留上一次成功输出；代码和数据在同次导出中替换，普通写入异常会回滚。编辑器崩溃或断电时不保证跨目录原子性。

## 升级

本包替代 com.novorizon.config。聚合安装器会在同一次 UPM 操作中移除旧包并安装本包；单包使用者需替换工程 manifest 的包名、Git 路径和 asmdef 引用。

旧 ConfigManager、ConfigTable、LoadJsonAsync、SetTable、LoadLubanAsync/GetLuban，以及 Framework Core 中的动态对象 DataManager 已删除。Luban 改用本包的 DataManager.LoadAsync/Get/TryGet。玩家等动态数据由业务自行维护；其他 JSON 可由业务加载 TextAsset 后直接反序列化。LocalStorageManager 的 JSON 存档 API 不受影响。

包和 AOT 程序集依赖已变化，HybridCLR 项目需要重新构建 Player。

生成器可通过 generatedAutoReferenced 配置生成 asmdef 的 autoReferenced。表结构需要热更时，将 codeOutput 放入项目热更目录并关闭自动引用，AOT 程序集不得直接依赖这些生成类型；具体加载放在游戏入口。GameStarter 已按此方式接线。
