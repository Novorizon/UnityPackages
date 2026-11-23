using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Game.Framework.Audio
{
    /// <summary>音源统一管理和回收。AudioClip 由调用者或资源系统持有，播放期间必须保持有效。</summary>
    public sealed partial class AudioManager : MonoSingleton<AudioManager>
    {
        /// <summary>在当前音频组件上播放，返回的句柄只控制此次播放。</summary>
        public static AudioHandle Play(
            AudioClip clip,
            AudioGroup group = AudioGroup.Sound,
            bool loop = false,
            float volume = 1,
            float pitch = 1,
            Vector3? position = null,
            Action<AudioHandle> completed = null) =>
            Instance.PlayInternal(clip, group, loop, volume, pitch, position, completed);

        /// <summary>尝试在当前音频组件上播放，返回的句柄绑定该组件。</summary>
        public static bool TryPlay(
            AudioClip clip,
            out AudioHandle handle,
            AudioGroup group = AudioGroup.Sound,
            bool loop = false,
            float volume = 1,
            float pitch = 1,
            Vector3? position = null,
            Action<AudioHandle> completed = null) =>
            Instance.TryPlayInternal(clip, out handle, group, loop, volume, pitch, position, completed);

        /// <summary>读取指定分组的音量系数，范围为 0 到 1。</summary>
        public static float GetGroupVolume(AudioGroup group) =>
            Instance.GetGroupVolumeInternal(group);

        /// <summary>修改分组音量并立即应用到已有播放，之后的新播放也使用该音量。</summary>
        public static void SetGroupVolume(AudioGroup group, float volume) =>
            Instance.SetGroupVolumeInternal(group, volume);

        /// <summary>暂停全部音源。全局暂停期间发起的新播放也会立即暂停。</summary>
        public static void PauseAll() =>
            Instance.PauseAllInternal();

        /// <summary>解除全局暂停，单独暂停的播放继续保持暂停。</summary>
        public static void ResumeAll() =>
            Instance.ResumeAllInternal();

        /// <summary>停止指定分组的播放，group 为 null 时停止所有分组。主动停止不触发自然播放完成回调。</summary>
        public static void StopAll(AudioGroup? group = null)
        {
            if (TryGetInstance(out var current))
            {
                current.StopAllInternal(group);
            }
        }

        /// <summary>路由同时作用于当前播放和之后的新播放，null 恢复直接输出。</summary>
        public static void SetMixerGroup(AudioGroup group, AudioMixerGroup mixerGroup) =>
            Instance.SetMixerGroupInternal(group, mixerGroup);

        /// <summary>在当前音频组件上切换音乐，返回的句柄绑定此次播放。</summary>
        public static AudioHandle PlayMusic(
            AudioClip clip,
            float fadeSeconds = 0.3f,
            float volume = 1,
            bool loop = true,
            AudioFadeCurve curve = AudioFadeCurve.EqualPower) =>
            Instance.PlayMusicInternal(clip, fadeSeconds, volume, loop, curve);

        /// <summary>停止 PlayMusic 创建的音乐，包括正在淡出的旧音乐；不影响普通 Music 分组播放。</summary>
        public static void StopMusic(float fadeSeconds = 0, AudioFadeCurve curve = AudioFadeCurve.EqualPower)
        {
            if (TryGetInstance(out var current))
            {
                current.StopMusicInternal(fadeSeconds, curve);
            }
        }

        /// <summary>手动模式的统一时钟。暂停期间不推进渐变；完成回调中的重入 Tick 会被忽略。</summary>
        public static void Tick(float deltaTime) =>
            Instance.TickInternal(deltaTime);

        public static int PlayingCount => TryGetInstance(out var current) ? current.playingCount : 0;

        public static int PooledCount => TryGetInstance(out var current) ? current.pooledCount : 0;

        public static bool IsPaused => TryGetInstance(out var current) ? current.paused : false;

        public static int MaxVoices
        {
            get => Instance.VoiceLimit;
            set => Instance.VoiceLimit = value;
        }

        public static float MasterVolume
        {
            get => Instance.Volume;
            set => Instance.Volume = value;
        }

        public static bool AutomaticUpdate
        {
            get => Instance.automaticUpdate;
            set => Instance.automaticUpdate = value;
        }

        public static bool UseUnscaledTime
        {
            get => Instance.useUnscaledTime;
            set => Instance.useUnscaledTime = value;
        }

        public static AudioHandle CurrentMusic => TryGetInstance(out var current) ? current.music : default;

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
        private int playingCount => voices.Count;
        private int pooledCount => pool.Count;

        /// <summary>同时播放的数量上限。降低上限时回收闲置对象，运行中的播放数量必须小于或等于新上限。</summary>
        private int VoiceLimit
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
        private float Volume
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

        protected override void OnStart()
        {
            Initialize();
        }

        public override void Initialize()
        {
            MainThread();
            initialized = true;
        }

        /// <summary>创建一次播放并返回控制句柄。传入 position 时启用 3D 音频，达到并发数量上限时抛出异常。</summary>
        private AudioHandle PlayInternal(AudioClip clip, AudioGroup group = AudioGroup.Sound, bool loop = false, float volume = 1, float pitch = 1, Vector3? position = null, Action<AudioHandle> completed = null)
        {
            if (!TryPlayInternal(clip, out var handle, group, loop, volume, pitch, position, completed))
            {
                throw new InvalidOperationException("Maximum concurrent audio voices reached.");
            }

            return handle;
        }

        /// <summary>申请播放实例并返回句柄，达到同时播放数量上限时返回 false。</summary>
        private bool TryPlayInternal(AudioClip clip, out AudioHandle handle, AudioGroup group = AudioGroup.Sound, bool loop = false, float volume = 1, float pitch = 1, Vector3? position = null, Action<AudioHandle> completed = null)
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
        private float GetGroupVolumeInternal(AudioGroup group)
        {
            ValidateGroup(group);
            return groupVolumes[(int)group];
        }

        /// <summary>修改分组音量并立即应用到已有播放，之后的新播放也使用该音量。</summary>
        private void SetGroupVolumeInternal(AudioGroup group, float volume)
        {
            MainThread();
            ValidateGroup(group);
            ValidateVolume(volume);
            groupVolumes[(int)group] = volume;
            ApplyVolumes();
        }

        /// <summary>暂停全部音源。全局暂停期间发起的新播放也会立即暂停。</summary>
        private void PauseAllInternal()
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
        private void ResumeAllInternal()
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
        private void StopAllInternal(AudioGroup? group = null)
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

        internal bool Contains(long id) => !IsDestroying && voices.ContainsKey(id);
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

        private void Update()
        {
            if (automaticUpdate && !IsDestroying)
            {
                TickInternal(useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
            }
        }

        private void ApplyVolumes()
        {
            foreach (var voice in voices.Values)
            {
                ApplyVolume(voice);
            }
        }

        private void ApplyVolume(Voice voice)
        {
            if (voice.Source != null)
            {
                voice.Source.volume = voice.Volume * masterVolume * groupVolumes[(int)voice.Group];
            }
        }

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

        private static void ValidateGroup(AudioGroup group)
        {
            if ((int)group < 0 || (int)group > (int)AudioGroup.UI)
            {
                throw new ArgumentOutOfRangeException(nameof(group));
            }
        }

        private static void ValidateVolume(float volume)
        {
            if (float.IsNaN(volume) || volume < 0 || volume > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(volume));
            }
        }

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

        protected override void OnExit()
        {
            ClearPlayback();
        }

    }
}
