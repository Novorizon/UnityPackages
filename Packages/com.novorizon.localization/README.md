# Novorizon Localization

只读翻译表、语言回退、参数格式化和可取消语言切换。小型本地表可以直接在 Inspector 配置，不需要 DataManager 或项目生成类型；大型配置仍可使用原有的 Excel / Luban 流程。

## 最少代码的本地表流程

1. 在 Excel 编辑翻译表，导出 UTF-8 CSV 或 TSV。第一行是 `key` 和标准语言代码；Key 保持稳定，业务配置保存 Key。正文可包含带引号的逗号、换行和双引号。示例：

   ```csv
   key,en,zh-Hans
   ui.confirm,Confirm,确定
   item.count,Items: {0},数量：{0}
   ```

2. 把文件放到项目 `Assets` 下，选中它，运行 `Assets > Novorizon > Localization > Create Setup From Selected CSV or TSV`。会在源文件旁生成独立的 `LocalizationTableAsset` 和 `LocalizationSettings`；同名输出已存在时不会覆盖。在已有 Table 的 Inspector 使用 `Import UTF-8 CSV / TSV` 更新数据。导入前先校验完整数据；解析失败会保留原表。
3. 在 Settings 选择默认语言、回退语言和是否跟随系统语言。默认及回退语言必须是表中的列。不支持的系统语言使用默认语言；例如 `fr-CA` 可匹配 `fr`，`zh-CN` / `zh-HK` 可分别匹配 `zh-Hans` / `zh-Hant`。
4. 在项目的持久入口对象添加 `LocalizationBootstrap`，指定 Settings。配置好的 Bootstrap 在 Awake 初始化，必须先于本地化界面打开。项目负责入口对象的跨场景存活；不要将它放在临时窗口上。默认销毁入口时释放它创建的服务；`Shutdown()` 可显式提前退出，旧入口不会释放后来替换的新服务。
5. 在 TextMeshProUGUI 添加 `LocalizedText`。Inspector 选择 `Preview Settings`，搜索 Key，再点击 `Assign Selected Key`；可切换预览语言并检查字体字符。Preview Settings 只用于编辑器，不写入文字组件，也不会创建运行时服务。静态 UI 不需要调用 DataManager 或 `Bind`。

也可用 `Create > Novorizon > Localization > Table / Settings` 手动创建资产。Table 的 Languages 顺序对应每行 Values 的顺序；CSV / TSV 适合较多文字。源表直接烘焙进资产，运行时不读取 CSV、XLSX 或生成表类，也不需要反射发现项目类型。修改资产不会更改已初始化的快照；由入口明确结束旧服务并重新初始化。项目负责语言偏好的存储。

已有代码入口可替代 Bootstrap，只初始化一次并持有返回的服务：

```csharp
using Game.Framework.Localization.Unity;

var localization = LocalizationSetup.Initialize(settings, preferredLanguage: savedLocale);
// 首次没有 savedLocale 时传 null，按 Settings 选择系统语言或默认语言。
// 在项目入口退出时调用 localization.Dispose()。
```

Setup 不会接管已经初始化的服务，重复初始化会报告错误。文字 OnEnable 和 Inspector 预览不负责初始化。先初始化服务，再启用界面。

## Excel / Luban 配置流程

1. 安装 framework.all，使用 `Novorizon/Localization/Create Luban Project`。
2. 编辑 `Config/Localization/Data/translations.xlsx` 的 key、zhHans、en 列。Key 保持稳定，业务配置保存 Key。
3. 选中 `Assets/Settings/LocalizationGenerator.asset`，运行 `Novorizon/Data/Luban/Generate`。复用 Data 包的官方 Luban CLI，输出独立的生成程序集、bytes 和清单，不改已有 Config/Luban 项目。
4. 在游戏入口加载并初始化；生成类型属于项目，接线代码放在业务程序集。

```csharp
using Game.Framework.Data;
using Game.Framework.Storage;
using Game.Framework.Localization;

var tables = await DataManager.Instance.LoadAsync(
    reader => new localizationSample.Tables(reader),
    manifestLocation: "Localization/manifest");
var catalog = LocalizationCatalog.FromRows(
    tables.TbTranslation.DataList,
    row => row.Key,
    new TranslationColumn<localizationSample.Translation>("zh-Hans", row => row.ZhHans),
    new TranslationColumn<localizationSample.Translation>("en", row => row.En));
LocalizationManager.Instance.Initialize(catalog, "zh-Hans", "en");
string caption = LocalizationManager.Instance.Get("ui.confirm");
string count = LocalizationManager.Instance.Format("item.count", 42);
```

上述示例使用默认 Resources 加载器；接入 YooAsset 时，沿用 DataManager 已配置的字节加载器，并让采集地址和清单 location 一致。默认读取全部语言。生成程序集需要被业务 asmdef 显式引用；如表放在热更程序集，初始化也放在热更入口。CSV / TSV 烘焙是另一条入口，可对接业务导出的标准文本表，不会自动反射 Luban 生成类型。

Catalog 会复制行内容，拒绝重复/空 Key、重复语言列、非法格式和不同语言的占位符编号差异。CSV / TSV 还拒绝列数不一致、Key 两端空白和破损引号。占位符为标准 .NET `{0}`、`{1:N2}` 等，最多编号 63；双大括号表示字面量。空翻译使用回退。查找顺序为当前语言及父语言、回退语言及父语言，最后返回 Key。数值格式使用当前语言文化设置。

Table Inspector 的 `Validate Keys / Formats / Missing Translations` 检查数据并列出空单元格；Settings Inspector 的 `Validate Startup Settings` 检查入口配置。空翻译可以用于回退，但验证仍会显示，避免英文回退掩盖漏翻译。LocalizedText 预览也会显示未知 Key 和空单元格。

## 切换与生命周期

```csharp
await LocalizationManager.Instance.SetLanguageAsync("en", async (language, token) =>
{
    await PrepareProjectFontsAndImagesAsync(language, token);
}, token);
// 成功后由项目持久化 locale；翻译正文不写入玩家存档。
LocalStorageManager.Instance.SaveText("settings/language.txt", "en");
```

PrepareProjectFontsAndImagesAsync 是项目方法：准备所需资源、持有其租约，失败时归还未发布资源。prepare 只准备，不提前替换可见 UI。准备成功且该请求仍为最新请求后，服务提交语言并触发 LanguageChanged。取消、准备失败、后发请求或 Dispose 都不能发布过期语言；不支持取消的底层资源操作可能继续，由项目加载器清理其结果。

同步 SetLanguage 适用于资源已就绪的切换。所有服务访问在已初始化的 Unity 主线程执行；原生 Unity 自动安装 UnityThread。初始化翻译后再打开界面。退出时在 UnityThread 停止前 Release；旧服务引用不可重用。

## TMP 文字和字体

安装 TextMeshPro 3.0.6 后启用独立的 Game.Framework.Localization.UGUI 程序集。TextMeshProUGUI 上添加 LocalizedText，在 Inspector 设置 Key 和 TMP_FontAsset 语言字体；或由业务调用 `label.Bind(LocalizationManager.Instance, "item.count", count)`。绑定复制参数，启用时刷新/订阅，禁用时退订，缓存界面重新启用后使用当前语言。

Font Overrides 按精确语言、父语言匹配，没有匹配时恢复该 TMP 文本的原字体资产。字体需已加载且由项目持有；组件不负责下载或释放字体。中文等语言需要项目提供覆盖所需字形的 TMP_FontAsset 和 fallback；默认拉丁字体不代表支持全部语言。复杂复数/性别、RTL 排版和按语言分包不包含在本版。

Inspector 的字符检查使用当前语言字体和字体 fallback，只读取已有字形，不动态加入字形或修改字体资产。检查的是当前正文的源字符（包括格式占位符和 rich text 标记）；TMP 3 的检查按 UTF-16 值进行，补充平面字符、实际排版、RTL 或 shaping 仍需原生验收。字体资产由项目准备，包内不附带第三方字体。

Unity 工程先执行 `Window > TextMeshPro > Import TMP Essential Resources`，配置 TMP Settings 的默认字体和项目字体 fallback。业务只在入口统一加载 Catalog；静态文字直接在组件填写 Key，动态文字使用 Bind 传入参数，不需要每个窗口访问 DataManager。

从旧版迁移：LocalizedText 类名、程序集和组件 GUID 保留；旧 UnityEngine.UI.Text 要替换为 TextMeshProUGUI，Font Overrides 重新指定 TMP_FontAsset。旧 Font 引用不能自动转换为 TMP 字体资产，业务 Prefab 和脚本字段需要显式迁移。

## 程序集与验证

`Game.Framework.Localization` 保持纯 Core 依赖；`Game.Framework.Localization.Unity` 提供 Unity 资产和显式入口，业务 asmdef 使用 Settings / Bootstrap 时引用后者。`Game.Framework.Localization.Editor` 提供 CSV / TSV 烘焙与验证。`Game.Framework.Localization.UGUI` 和 `Game.Framework.Localization.TMP.Editor` 只在安装 TMP 3.0.6 或更高版本时启用。Luban 菜单仍放在可选的 `Game.Framework.Localization.Luban.Editor`，本地表不依赖 Data 包。

仓库回归使用官方 Luban CLI 从本模板 Excel 生成 C# 和 bytes，覆盖读取、快照复制、格式校验、回退、失败/取消/后发请求、退出及 uGUI 绑定清理。`tools/LocalizationSetupVerification` 独立验证 CSV / TSV 导入、缺失单元格和快照隔离；原生 `LocalizationSetupTests` 验证 Settings、Bootstrap 所有权及 Inspector Key 绑定。托管测试不替代字体字形和真实布局验收。
