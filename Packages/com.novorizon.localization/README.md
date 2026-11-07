# Novorizon Localization

Luban 友好的只读翻译表、语言回退、参数格式化和可取消语言切换。运行时只依赖 Framework，不引用项目生成的表类型、YooAsset 或 UI。

## 从 Excel 开始

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

上述示例使用默认 Resources 加载器；接入 YooAsset 时，沿用 DataManager 已配置的字节加载器，并让采集地址和清单 location 一致。默认读取全部语言，适合首版小型表。生成程序集需要被业务 asmdef 显式引用；如表放在热更程序集，初始化也放在热更入口。

Catalog 会复制行内容，拒绝重复/空 Key、重复语言列、非法格式和不同语言的占位符编号差异。占位符为标准 .NET `{0}`、`{1:N2}` 等，最多编号 63；双大括号表示字面量。空翻译使用回退。查找顺序为当前语言及父语言、默认语言及父语言，最后返回 Key。数值格式使用当前语言文化设置。

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

## uGUI 文字和字体

安装 uGUI 后启用独立的 Game.Framework.Localization.UGUI 程序集。Text 上添加 LocalizedText，设置 Key 和语言字体；或由业务调用 `label.Bind(LocalizationManager.Instance, "item.count", count)`。绑定复制参数，启用时刷新/订阅，禁用时退订，缓存界面重新启用后使用当前语言。

Font Overrides 按精确语言、父语言匹配，没有匹配时恢复该 Text 原字体。字体需已加载且由项目持有；组件不负责下载或释放字体。TMP、复杂复数/性别、RTL 排版和按语言分包不包含在本版。

## 验证

仓库回归使用官方 Luban CLI 从本模板 Excel 生成 C# 和 bytes，覆盖读取、快照复制、格式校验、回退、失败/取消/后发请求、退出及 uGUI 绑定清理。测试模型不替代字体字形和真实布局验收。
