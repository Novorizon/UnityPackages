# Novorizon Config

独立配置包，依赖 Framework。默认使用 Resources 文本，也可传入项目的异步文本加载函数。未绑定 Luban 等配置生成工具。

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
