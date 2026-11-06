# 游戏基础服务

Framework 内提供场景、运行时数据与本地存储；音频为 com.novorizon.audio，配置为 com.novorizon.config。各自复用 Framework 单例，不依赖 ECS/Ability，不包含玩家、背包或关卡业务。framework.all 的目录包含这两个新包及 Unity Audio 模块。

## 初始化

GameStarter 的 GameEntry 按资源、数据/存储、场景、音频、配置、UI/GPU/ECS 的顺序准备服务，然后启动 StartupModule。GameSession 保持原来的模块生命周期。

```csharp
using Game.Framework.Audio;
using Game.Framework.Config;
using Game.Framework.Data;
using Game.Framework.Storage;
using GameSceneManager = Game.Framework.Scenes.SceneManager;

_ = DataManager.Instance;
LocalStorageManager.Instance.Initialize();
GameSceneManager.Instance.Initialize();
AudioManager.Instance.Initialize();
ConfigManager.Instance.Initialize();
```

## 场景

默认场景必须加入 Build Settings；传入名称或路径。资源包场景在 ResourceManager.InitializeAsync 成功后，将 ResourceManager.Instance.CreateSceneLoader() 传给场景管理器 Initialize。GameStarter 开启 initializeYooAsset 时已接线，不需要复制或修改 YooAsset。

```csharp
var world = await GameSceneManager.Instance.LoadAsync("World", UnityEngine.SceneManagement.LoadSceneMode.Additive);
await GameSceneManager.Instance.UnloadAsync(world);
```

加载/卸载排队执行，支持 IProgress<float> 和 CancellationToken。取消队列中尚未执行的请求会跳过；取消已开始请求的等待不会撤销原生操作，场景仍可能切换，后续操作仍等待底层结束。这是 Unity 原生加载的限制。不要将取消等待当作场景回滚。

管理器只卸载自己加载的场景。Dispose 停止新请求与通知并释放加载器持有的句柄，不自动卸载当前场景。原生加载不能强制中断，退出期间已开始的操作仍可能完成。

## 运行时数据

DataManager 按声明类型注册普通对象。Register/Get/TryGet/Remove/Clear 不做反射扫描或自动业务初始化。

```csharp
DataManager.Instance.Register(new RuntimeData());
var data = DataManager.Instance.Get<RuntimeData>();
DataManager.Instance.Clear();
```

RuntimeData 由项目定义。默认拥有注册对象，Remove/Clear/Dispose 会调用其 IDisposable；传入 owned:false 时由调用方释放。逆注册顺序清理，一个对象失败仍继续清理其他对象，最后抛出 AggregateException。注销或重新开局时何时 Clear 由项目流程决定，不等同于游戏退出。

## 配置

配置是只读表，运行时数据是游戏运行中会改变的状态。ConfigManager.LoadJsonAsync 使用 {"items":[...]} 格式并校验键；SetTable 可直接接入配置生成工具。详见配置包 README。

GameStarter 默认从 Resources 加载；开启 YooAsset 后读取 TextAsset.text。此适配使用现有 ResourceManager 的共享缓存，配置管理器不会调用 ReleaseHandle 影响其他消费者；资源包缓存的释放仍由资源管理生命周期负责。

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
