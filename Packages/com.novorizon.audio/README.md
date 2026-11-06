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

## 音乐切换与渐变

```csharp
var audio = AudioManager.Instance;
var music = audio.PlayMusic(firstClip, fadeSeconds: 0.5f);
audio.PlayMusic(secondClip, fadeSeconds: 1); // 默认等功率交叉渐变
music.FadeOut(0.3f);                       // 结束后回收
audio.StopMusic(0.5f);                     // 包含正在淡出的旧音乐
audio.SetMixerGroup(AudioGroup.Music, musicMixerGroup);
```

`FadeTo`/`FadeOut` 支持 Linear、SmoothStep、EqualPower。渐变操作替换该声音当前的渐变；SetVolume 立即取消渐变。分组/主音量继续相乘。暂停声音、PauseAll 或 AudioListener.pause 都暂停渐变。默认统一 Update 使用 unscaledDeltaTime；`AutomaticUpdate = false` 后由项目调用 `Tick(deltaTime)`，可配置 UseUnscaledTime。

PlayMusic 管理一个主音乐和正在淡出的旧音乐，重复播放同一主音乐会复用句柄并调整音量。交叉渐变需要额外音源槽位，容量不足会抛出异常且保留当前音乐。零秒切换先停止旧音乐再播放。StopMusic 只影响 PlayMusic 创建的声音，普通 Music 分组播放通过句柄或 StopAll 控制。

AudioMixer 路由同时作用于已有和新声音，传 null 恢复直接输出。池复用 AudioSource 和管理用 Voice，统一 Tick 在集合预热后不产生每帧托管分配。自然完成会先回收再通知；淡出停止不发自然完成通知。后台加载中的 AudioClip 在开始播放前不会被误判为结束，也不推进渐变。
