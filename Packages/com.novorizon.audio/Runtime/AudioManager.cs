using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Framework.Audio
{
    /// <summary>音源统一管理和回收。AudioClip 由调用者或资源系统持有，播放期间必须保持有效。</summary>
    /// <remarks>集中管理播放实例、音量分组和 AudioSource 复用。</remarks>
    public sealed partial class AudioManager : MonoSingleton<AudioManager>
    {
        /// <summary>记录一次播放的音源、分组、状态和完成回调。对象回收入池后可供其他播放复用。</summary>
        private sealed class Voice
        {
            internal AudioSource Source;
            internal AudioGroup Group;
            internal float Volume;
            internal bool Paused;
            internal Action<AudioHandle> Completed;
            internal FadeState Fade;
            internal bool Music;
        }

        private readonly Dictionary<long, Voice> voices = new Dictionary<long, Voice>();
        private readonly Stack<AudioSource> pool = new Stack<AudioSource>();
        private readonly List<(long Id, bool Natural)> finished = new List<(long, bool)>();
        private readonly Stack<Voice> voicePool = new Stack<Voice>();
        private readonly float[] groupVolumes =
        {
            1,
            1,
            1,
            1
        };
        private long nextId;
        private bool initialized;
        private bool paused;
        private float masterVolume = 1;
        private int maxVoices = 32;
        public int PlayingCount => voices.Count;
        public int PooledCount => pool.Count;
        public bool IsPaused => paused;

        /// <summary>同时播放的数量上限。降低上限时回收闲置对象，运行中的播放数量必须小于或等于新上限。</summary>
        public int MaxVoices
        {
            get => maxVoices;
            set
            {
                MainThread();
                if (value < 1 || value < voices.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                maxVoices = value;
                while (pool.Count + voices.Count > maxVoices)
                {
                    var source = pool.Pop();
                    if (source != null)
                    {
                        Destroy(source.gameObject);
                    }
                }

                while (voicePool.Count + voices.Count > maxVoices)
                {
                    voicePool.Pop();
                }
            }
        }

        /// <summary>总音量，取值为 0 到 1。修改时立即更新当前所有音源。</summary>
        public float MasterVolume
        {
            get => masterVolume;
            set
            {
                MainThread();
                ValidateVolume(value);
                masterVolume = value;
                ApplyVolumes();
            }
        }

        /// <summary>组件开始运行时打开音频播放入口。</summary>
        protected override void OnStart()
        {
            Initialize();
        }

        /// <summary>在 Unity 主线程启用播放，可重复调用。</summary>
        public override void Initialize()
        {
            MainThread();
            initialized = true;
        }

        /// <summary>创建一次播放并返回控制句柄。传入 position 时启用 3D 音频，达到并发数量上限时抛出异常。</summary>
        public AudioHandle Play(AudioClip clip, AudioGroup group = AudioGroup.Sound, bool loop = false, float volume = 1, float pitch = 1, Vector3? position = null, Action<AudioHandle> completed = null)
        {
            if (!TryPlay(clip, out var handle, group, loop, volume, pitch, position, completed))
            {
                throw new InvalidOperationException("Maximum concurrent audio voices reached.");
            }

            return handle;
        }

        /// <summary>申请播放实例并返回句柄，达到同时播放数量上限时返回 false。</summary>
        public bool TryPlay(AudioClip clip, out AudioHandle handle, AudioGroup group = AudioGroup.Sound, bool loop = false, float volume = 1, float pitch = 1, Vector3? position = null, Action<AudioHandle> completed = null)
        {
            MainThread();
            if (!initialized)
            {
                throw new InvalidOperationException("Initialize AudioManager first.");
            }

            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }

            ValidateGroup(group);
            ValidateVolume(volume);
            if (float.IsNaN(pitch) || pitch <= 0 || pitch > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(pitch));
            }

            handle = default;
            if (voices.Count >= maxVoices)
            {
                return false;
            }

            AudioSource source = null;
            while (pool.Count != 0 && source == null)
            {
                source = pool.Pop();
            }

            if (source == null)
            {
                var child = new GameObject("Audio Voice");
                child.transform.SetParent(transform, false);
                source = child.AddComponent<AudioSource>();
            }

            source.playOnAwake = false;
            source.clip = clip;
            source.loop = loop;
            source.pitch = pitch;
            source.spatialBlend = position.HasValue ? 1 : 0;
            source.transform.position = position ?? transform.position;
            var voice = voicePool.Count == 0 ? new Voice() : voicePool.Pop();
            voice.Source = source;
            voice.Group = group;
            voice.Volume = volume;
            voice.Completed = completed;
            voice.Paused = false;
            voice.Music = false;
            voice.Fade = default;
            source.outputAudioMixerGroup = mixerGroups[(int)group];
            // 音源可以重复使用，播放 ID 始终递增，旧句柄不会控制复用后的音源。
            long id = ++nextId;
            voices.Add(id, voice);
            ApplyVolume(voice);
            source.Play();
            if (paused)
            {
                source.Pause();
            }

            handle = new AudioHandle(this, id);
            return true;
        }

        /// <summary>读取指定分组的音量系数，范围为 0 到 1。</summary>
        public float GetGroupVolume(AudioGroup group)
        {
            ValidateGroup(group);
            return groupVolumes[(int)group];
        }

        /// <summary>修改分组音量并立即应用到已有播放，之后的新播放也使用该音量。</summary>
        public void SetGroupVolume(AudioGroup group, float volume)
        {
            MainThread();
            ValidateGroup(group);
            ValidateVolume(volume);
            groupVolumes[(int)group] = volume;
            ApplyVolumes();
        }

        /// <summary>暂停全部音源。全局暂停期间发起的新播放也会立即暂停。</summary>
        public void PauseAll()
        {
            MainThread();
            if (paused)
            {
                return;
            }

            paused = true;
            foreach (var voice in voices.Values)
            {
                if (voice.Source != null)
                {
                    voice.Source.Pause();
                }
            }
        }

        /// <summary>解除全局暂停，单独暂停的播放继续保持暂停。</summary>
        public void ResumeAll()
        {
            MainThread();
            if (!paused)
            {
                return;
            }

            paused = false;
            foreach (var voice in voices.Values)
            {
                if (!voice.Paused && voice.Source != null)
                {
                    voice.Source.UnPause();
                }
            }
        }

        /// <summary>停止指定分组的播放，group 为 null 时停止所有分组。主动停止不触发自然播放完成回调。</summary>
        public void StopAll(AudioGroup? group = null)
        {
            MainThread();
            if (group.HasValue)
            {
                ValidateGroup(group.Value);
            }

            foreach (long id in new List<long>(voices.Keys))
            {
                if (!group.HasValue || voices[id].Group == group.Value)
                {
                    Stop(id);
                }
            }
        }

        // 检查播放 ID 仍然有效。回收音源不会恢复旧 ID。
        internal bool Contains(long id) => !IsDestroying && voices.ContainsKey(id);
        // 结束指定播放，清掉剪辑和回调，再将音源与记录归还对象池。
        internal void Stop(long id)
        {
            MainThread();
            if (voices.TryGetValue(id, out var voice))
            {
                voices.Remove(id);
                if (musicId == id)
                {
                    musicId = 0;
                }

                voice.Completed = null;
                voice.Fade = default;
                voice.Music = false;
                if (voice.Source != null)
                {
                    voice.Source.Stop();
                    voice.Source.clip = null;
                    voice.Source.outputAudioMixerGroup = null;
                    pool.Push(voice.Source);
                }

                voice.Source = null;
                voicePool.Push(voice);
            }
        }

        // 设置单次播放的暂停状态。恢复时仍需检查全局暂停。
        internal void Pause(long id, bool value)
        {
            MainThread();
            if (!voices.TryGetValue(id, out var voice) || voice.Source == null)
            {
                return;
            }

            voice.Paused = value;
            if (value)
            {
                voice.Source.Pause();
            }
            else if (!paused)
            {
                voice.Source.UnPause();
            }
        }

        // 立即设置单次播放音量，并取消该播放之前的渐变。
        internal void SetVolume(long id, float volume)
        {
            MainThread();
            ValidateVolume(volume);
            if (voices.TryGetValue(id, out var voice))
            {
                voice.Fade = default;
                voice.Volume = volume;
                ApplyVolume(voice);
            }
        }

        // 自动模式下使用所选的 Unity 帧时间推进播放。
        private void Update()
        {
            if (AutomaticUpdate && !IsDestroying)
            {
                Tick(UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
            }
        }

        // 总音量或分组音量改变后，重新计算当前各音源的有效音量。
        private void ApplyVolumes()
        {
            foreach (var voice in voices.Values)
            {
                ApplyVolume(voice);
            }
        }

        // 合成全局、分组和单个播放实例的音量，再写入 AudioSource。
        private void ApplyVolume(Voice voice)
        {
            if (voice.Source != null)
            {
                voice.Source.volume = voice.Volume * masterVolume * groupVolumes[(int)voice.Group];
            }
        }

        // 拒绝从后台线程或已销毁的管理器操作 Unity 音源。
        private void MainThread()
        {
            if (IsDestroying)
            {
                throw new ObjectDisposedException(nameof(AudioManager));
            }

            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("AudioManager must run on Unity's initialized main thread.");
            }
        }

        // 拒绝无效枚举值，避免用它索引分组配置。
        private static void ValidateGroup(AudioGroup group)
        {
            if ((int)group < 0 || (int)group > (int)AudioGroup.UI)
            {
                throw new ArgumentOutOfRangeException(nameof(group));
            }
        }

        // 音量必须在 0 到 1 之间，NaN 也视为无效。
        private static void ValidateVolume(float volume)
        {
            if (float.IsNaN(volume) || volume < 0 || volume > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(volume));
            }
        }

        // 销毁在用和池中的音源，清理背景音乐与暂停状态。
        private void ClearPlayback()
        {
            foreach (var voice in voices.Values)
            {
                if (voice.Source != null)
                {
                    voice.Source.Stop();
                    Destroy(voice.Source.gameObject);
                }
            }

            voices.Clear();
            voicePool.Clear();
            musicId = 0;
            while (pool.Count != 0)
            {
                var source = pool.Pop();
                if (source != null)
                {
                    Destroy(source.gameObject);
                }
            }

            initialized = false;
            paused = false;
        }

        /// <summary>组件生命周期结束时清理运行中播放及音源池。</summary>
        protected override void OnExit()
        {
            ClearPlayback();
        }

    }
}
