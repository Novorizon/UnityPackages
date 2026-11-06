# 游戏基础服务

Framework 内提供场景与本地存储；音频为 com.novorizon.audio，Luban 数据表为 com.novorizon.data。各自复用 Framework 单例，不依赖 ECS/Ability，不包含玩家、背包或关卡业务。输入为 com.novorizon.input；framework.all 的目录包含三个服务包、官方 Luban 运行时，以及 Unity Audio/Input System 模块。

## 初始化

GameStarter 默认由 GameEntry 准备资源并加载热更，HotGame 按存储、输入、场景、音频、数据表加载器、UI/GPU/ECS 的顺序准备普通服务，再启动可选 StartupModule 和业务。关闭热更时由 GameEntry 的同名函数执行这套初始化；GameSession 只协调可选模块的生命周期。

```csharp
using Game.Framework.Audio;
using Game.Framework.Data;
using Game.Framework.Storage;
using GameSceneManager = Game.Framework.Scenes.SceneManager;

LocalStorageManager.Instance.Initialize();
GameSceneManager.Instance.Initialize();
AudioManager.Instance.Initialize();
DataManager.Instance.Initialize();
```

## 场景

默认场景必须加入 Build Settings；传入名称或路径。资源包场景在 ResourceManager.InitializeAsync 成功后，将 ResourceManager.Instance.CreateSceneLoader() 传给场景管理器 Initialize。GameStarter 开启 initializeYooAsset 时已接线，不需要复制或修改 YooAsset。

```csharp
var world = await GameSceneManager.Instance.LoadAsync("World", UnityEngine.SceneManagement.LoadSceneMode.Additive);
await GameSceneManager.Instance.UnloadAsync(world);
```

加载/卸载排队执行，支持 IProgress<float> 和 CancellationToken。取消队列中尚未执行的请求会跳过；取消已开始请求的等待不会撤销原生操作，场景仍可能切换，后续操作仍等待底层结束。这是 Unity 原生加载的限制。不要将取消等待当作场景回滚。

管理器只卸载自己加载的场景。Dispose 停止新请求与通知并释放加载器持有的句柄，不自动卸载当前场景。原生加载不能强制中断，退出期间已开始的操作仍可能完成。

## Luban 数据表

DataManager 位于独立 com.novorizon.data 包中，只负责 Luban 生成的表。无需注册；加载时传入生成的 Tables 构造方式：

```csharp
var tables = await DataManager.Instance.LoadAsync(reader => new cfg.Tables(reader));
var row = tables.TbExample[1];
var current = DataManager.Instance.Get<cfg.Tables>();
```

生成的 Tables 负责具体表和引用解析。DataManager 负责异步读取与完整快照替换；加载失败保留上一次成功结果。GameStarter 默认从 Resources 加载；开启 YooAsset 后，清单和表文件共用一个字节加载器，每次读取使用独立 AcquireAsync<TextAsset> 租约并及时释放。生成和程序集引用详见 [Data 包说明](../Packages/com.novorizon.data/README.md)。

玩家属性、背包等动态数据由业务层维护，不登记到 DataManager。其他 JSON 文件也由业务按具体结构读取、反序列化；框架不再提供通用 JSON 表包装。版本化 JSON 存档仍由 LocalStorageManager 提供。

## 本地存储

默认目录为 Application.persistentDataPath/Storage。Initialize(directory) 可指定测试或项目目录；一个实例不能中途换目录。

```csharp
LocalStorageManager.Instance.SaveJson("settings.json", data, version: 1);
var restored = LocalStorageManager.Instance.LoadJson<RuntimeData>("settings.json", version: 1);
```

支持 Bytes/Text/Json、Exists、Delete。JSON 使用 JsonUtility 字段序列化，记录包含版本号；迁移回调只转换内存结果，项目明确 SaveJson 后才覆盖旧文件。未来版本、无迁移函数或坏数据会报错，不默认清空存档。

写入先保存同目录临时文件并 Flush，再替换主文件，保留 .bak；主文件缺失时读取备份。平台不支持 File.Replace 时使用备份再重命名，存在两个重命名之间的恢复窗口，不宣称所有平台都具有相同原子性。事务文件后缀保留，拒绝绝对路径、上级目录和 Windows 路径别名。

当前是小文件同步存储，不用于逐帧写入、数据库或大资源；不提供加密或防作弊。WebGL 文件系统持久化和移动设备断电行为仍需按目标平台补验。

## 音频

AudioManager 管理可复用音源，AudioHandle 管理一次播放。支持 Music/Sound/Voice/UI 分组、主音量、单次音量、2D/固定位置 3D、暂停恢复与自然完成通知。AudioClip 的加载和持有交给资源系统，场景中保留一个 AudioListener。详见音频包 README。

输入包管理项目 .inputactions 的运行时副本、控制方案、设备过滤、嵌套屏蔽、订阅和交互式重绑定，保存绑定由项目调用本地存储。音频增加淡入淡出、等功率音乐交叉渐变、AudioMixer 分组路由和统一 Tick，复用音源及管理对象。详见各包 README。
