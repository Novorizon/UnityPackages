# Novorizon Config

独立配置包，依赖 Framework 与官方 Luban Unity 运行时 1.2.0。支持普通 JSON 表和 Luban 生成的 Tables；默认使用 Resources，可注入异步文本和二进制加载函数。

JSON 格式：

```json
{"items":[{"id":1,"label":"example"}]}
```

记录类型必须符合 Unity JsonUtility 规则：Serializable 普通类、可序列化字段；不是属性或 Dictionary。自定义生成器可以直接调用 SetTable，无需经过 JSON。

```csharp
using System;
using Game.Framework.Config;

[Serializable]
public sealed class ExampleConfig
{
    public int id;
    public string label;
}
```

```csharp
ConfigManager.Instance.Initialize();
await ConfigManager.Instance.LoadJsonAsync<int, ExampleConfig>("Config/Example", row => row.id);
var row = ConfigManager.Instance.Get<int, ExampleConfig>(1);
```

Resources 路径不含扩展名。可用 `Initialize((location, token) => ...)` 接入 YooAsset 或其他来源；加载来源只初始化一次，需要切换时先 Reset。

整表通过空记录、空键、重复键校验后才发布。加载失败保留上一张有效表；同类型后发请求优先，旧请求晚完成会取消而不覆盖新结果。取消、Clear 和 Dispose 后不能发布旧结果。表的索引和集合只读，记录对象也应作为只读配置使用。

每组记录类型和键类型对应一张表；支持 int/string 等键。配置加载与查询应在游戏主线程调用，不负责创建玩家数据或保存存档。验证使用 Unity API 模型，JsonUtility 与 IL2CPP 泛型序列化需在目标 Unity 工程验收。

## Luban

通过 framework.all 自动解析官方 `com.code-philosophy.luban`；单独 Git 安装时，也在工程 manifest 声明官方来源：

```json
"com.code-philosophy.luban": "https://github.com/focus-creative-games/luban_unity.git#061314200e2f03307c4d90b448e1de7b85431905"
```

1. 安装 Git 和 **.NET 8 SDK**，重启 Unity/Hub，让编辑器进程能找到 `git`、`dotnet`。
2. 使用 **Novorizon → Config → Luban → Create Project**。项目根目录生成 `Config/Luban`，其中有 `luban.conf`、XML 定义和可编辑的 `Data/example.xlsx`；Settings 资产保存在 `Assets/Settings/LubanGenerator.asset`。
3. 修改 Excel 后使用 **Generate**。首次自动取得固定版本官方 Luban 4.12.0 并编译，工具只缓存到当前项目 `Library/Novorizon/Luban`。也可在 Settings 指定已有的 Luban.dll 和 dotnet 路径。
4. 输出 C# 到 `Assets/Generated/Config`，`.bytes` 和 `manifest.json` 到 `Assets/Resources/Config/Luban`。生成程序集 `Game.Config.Generated` 引用 `Luban.Runtime`。调用生成类型的自定义 asmdef 必须直接引用这两个程序集；调用 ConfigManager 还需要 `Game.Framework.Config`。

```csharp
ConfigManager.Instance.Initialize(); // GameStarter 已初始化，无需重复指定加载器
var tables = await ConfigManager.Instance.LoadLubanAsync(load => new cfg.Tables(load));
var example = tables.TbExample[1];
var current = ConfigManager.Instance.GetLuban<cfg.Tables>();
```

先异步预取清单中的二进制，再调用 Luban 生成的同步 Tables 构造函数，自动执行生成代码中的引用解析。构建完整快照后才替换旧 Tables；错误、取消、Clear、Dispose 或过期请求都不会覆盖当前有效配置。每个数据流使用独立 ByteBuf 游标。

输出目录完全由生成器管理，不能混放手写文件。首次只能写入空目录；后续按标记识别自身输出，移除退役文件并保留仍存在文件/文件夹的 Unity `.meta`。CLI 失败保留上一次成功输出；代码与数据在同次导出中替换，普通写入异常会回滚。不承诺编辑器进程崩溃或断电时的跨目录原子性。

YooAsset 使用 `Initialize(textLoader, bytesLoader)`；GameStarter 已用 `AcquireAsync<TextAsset>` 的独立租约分别读取 `.text`/`.bytes` 并释放。自定义采集规则应让地址与清单的 `location` 一致，或在 Settings 修改 `locationPrefix`。Resources 地址不含扩展名。官方运行时和 CLI 源码不进入本包。
