using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Framework.Audio
{
    /// <summary>音源统一管理和回收。AudioClip 由调用者或资源系统持有，播放期间必须保持有效。</summary>
    public sealed partial class AudioManager : MonoSingleton<AudioManager>
    {
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
        private static AudioManager lifetimeOwner;
        private long nextId;
        private bool initialized;
        private bool paused;
        private float masterVolume = 1;
        private int maxVoices = 32;
        public int PlayingCount => voices.Count;
        public int PooledCount => pool.Count;
        public bool IsPaused => paused;

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

        protected override void OnStart()
        {
            lifetimeOwner = this;
            Initialize();
        }

        public void Initialize()
        {
            MainThread();
            initialized = true;
        }

        public AudioHandle Play(AudioClip clip, AudioGroup group = AudioGroup.Sound, bool loop = false, float volume = 1, float pitch = 1, Vector3? position = null, Action<AudioHandle> completed = null)
        {
            if (!TryPlay(clip, out var handle, group, loop, volume, pitch, position, completed))
            {
                throw new InvalidOperationException("Maximum concurrent audio voices reached.");
            }

            return handle;
        }

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

        public float GetGroupVolume(AudioGroup group)
        {
            ValidateGroup(group);
            return groupVolumes[(int)group];
        }

        public void SetGroupVolume(AudioGroup group, float volume)
        {
            MainThread();
            ValidateGroup(group);
            ValidateVolume(volume);
            groupVolumes[(int)group] = volume;
            ApplyVolumes();
        }

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
            if (AutomaticUpdate && !IsDestroying)
            {
                Tick(UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlay()
        {
            if (lifetimeOwner != null)
            {
                lifetimeOwner.ClearPlayback();
                lifetimeOwner.masterVolume = 1;
                lifetimeOwner.maxVoices = 32;
                lifetimeOwner.AutomaticUpdate = true;
                lifetimeOwner.UseUnscaledTime = true;
                Array.Clear(lifetimeOwner.mixerGroups, 0, lifetimeOwner.mixerGroups.Length);
                for (int i = 0; i < lifetimeOwner.groupVolumes.Length; i++)
                {
                    lifetimeOwner.groupVolumes[i] = 1;
                }
            }

            lifetimeOwner = null;
            ResetSingletonReference();
        }
    }
}
