# Novorizon Audio

独立音频包，依赖 Framework；不依赖 UI、ECS 或 YooAsset。Git 单包安装前需先安装匹配版本的 Framework，也可使用 framework.all 聚合安装。

```csharp
using Game.Framework.Audio;

AudioManager.Instance.Initialize();
var handle = AudioManager.Instance.Play(clip, AudioGroup.Music, loop: true);
AudioManager.Instance.MasterVolume = 0.8f;
AudioManager.Instance.SetGroupVolume(AudioGroup.Music, 0.5f);
handle.Pause();
handle.Resume();
handle.Stop();
```

`Play` 接受调用方已加载的 AudioClip。播放期间由调用方保持资源有效；管理器仅持有音源，不卸载共享资源。场景需要一个启用的 AudioListener，通常挂在游戏摄像机上。

默认最多同时播放 32 个声音，`MaxVoices` 可调整。达到上限时 `TryPlay` 返回 false，`Play` 抛出异常。传入 `position` 为固定位置 3D 音源，否则为 2D；声音结束或 Stop 后回收音源，旧句柄不能操作新播放。自然完成才调用 completed，手动 Stop 不调用。

主音量 × 分组音量 × 单次播放音量构成最终音量。PauseAll/ResumeAll 保留单独暂停的状态。销毁和再次进入 Play 会清理音源及播放状态。所有播放与控制 API 应在 Unity 主线程使用。

验证使用实际管理器源码与 Unity API 模型；声音输出、压缩/流式 AudioClip、移动设备暂停与原生回收仍需在 Unity 验收。
