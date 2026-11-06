using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Game.Framework.Audio
{
    public enum AudioFadeCurve
    {
        Linear,
        SmoothStep,
        EqualPower
    }

    public sealed partial class AudioManager
    {
        private struct FadeState
        {
            internal bool Active, StopAtEnd;
            internal float Start, Target, Duration, Elapsed;
            internal AudioFadeCurve Curve;
        }

        private readonly AudioMixerGroup[] mixerGroups = new AudioMixerGroup[4];
        private long musicId;
        private bool ticking;
        public bool AutomaticUpdate { get; set; } = true;
        public bool UseUnscaledTime { get; set; } = true;
        public AudioHandle CurrentMusic => Contains(musicId) ? new AudioHandle(this, musicId) : default;

        /// <summary>路由同时作用于当前播放和之后的新播放，null 恢复直接输出。</summary>
        public void SetMixerGroup(AudioGroup group, AudioMixerGroup mixerGroup)
        {
            MainThread();
            ValidateGroup(group);
            mixerGroups[(int)group] = mixerGroup;
            foreach (var voice in voices.Values)
            {
                if (voice.Group == group && voice.Source != null)
                {
                    voice.Source.outputAudioMixerGroup = mixerGroup;
                }
            }
        }

        /// <summary>播放唯一的主音乐。渐变切换需要两个音源槽位，容量不足时保持原音乐。</summary>
        public AudioHandle PlayMusic(AudioClip clip, float fadeSeconds = 0.3f, float volume = 1, bool loop = true, AudioFadeCurve curve = AudioFadeCurve.EqualPower)
        {
            MainThread();
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }

            ValidateVolume(volume);
            ValidateFade(fadeSeconds, curve);
            if (voices.TryGetValue(musicId, out var current) && current.Source != null && current.Source.clip == clip && current.Source.loop == loop)
            {
                FadeTo(musicId, volume, fadeSeconds, false, curve);
                return CurrentMusic;
            }

            if (fadeSeconds == 0)
            {
                StopMusic();
            }

            long previous = musicId;
            // 必须先成功获取新音源，再改变旧音乐的渐变。
            var next = Play(clip, AudioGroup.Music, loop, fadeSeconds == 0 ? volume : 0);
            voices[next.Id].Music = true;
            musicId = next.Id;
            if (fadeSeconds != 0)
            {
                FadeTo(next.Id, volume, fadeSeconds, false, curve);
                FadeTo(previous, 0, fadeSeconds, true, curve);
            }

            return next;
        }

        /// <summary>停止 PlayMusic 创建的音乐，包括正在淡出的旧音乐；不影响普通 Music 分组播放。</summary>
        public void StopMusic(float fadeSeconds = 0, AudioFadeCurve curve = AudioFadeCurve.EqualPower)
        {
            MainThread();
            ValidateFade(fadeSeconds, curve);
            var ids = new System.Collections.Generic.List<long>();
            foreach (var pair in voices)
            {
                if (pair.Value.Music)
                {
                    ids.Add(pair.Key);
                }
            }

            musicId = 0;
            foreach (long id in ids)
            {
                FadeTo(id, 0, fadeSeconds, true, curve);
            }
        }

        internal bool FadeTo(long id, float volume, float seconds, bool stopAtEnd, AudioFadeCurve curve)
        {
            MainThread();
            ValidateVolume(volume);
            ValidateFade(seconds, curve);
            if (!voices.TryGetValue(id, out var voice))
            {
                return false;
            }

            if (seconds == 0)
            {
                voice.Fade = default;
                voice.Volume = volume;
                ApplyVolume(voice);
                if (stopAtEnd)
                {
                    Stop(id);
                }

                return true;
            }

            voice.Fade = new FadeState
            {
                Active = true,
                Start = voice.Volume,
                Target = volume,
                Duration = seconds,
                StopAtEnd = stopAtEnd,
                Curve = curve
            };
            return true;
        }

        /// <summary>手动模式的统一时钟。暂停期间不推进渐变；完成回调中的重入 Tick 会被忽略。</summary>
        public void Tick(float deltaTime)
        {
            MainThread();
            if (deltaTime < 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            if (ticking || paused || AudioListener.pause)
            {
                return;
            }

            ticking = true;
            try
            {
                finished.Clear();
                foreach (var pair in voices)
                {
                    var voice = pair.Value;
                    if (voice.Source == null)
                    {
                        finished.Add((pair.Key, true));
                        continue;
                    }

                    if (voice.Paused)
                    {
                        continue;
                    }

                    if (!voice.Source.isPlaying && voice.Source.clip != null && voice.Source.clip.loadState == AudioDataLoadState.Loading)
                    {
                        continue;
                    }

                    if (voice.Fade.Active && AdvanceFade(voice, deltaTime))
                    {
                        finished.Add((pair.Key, false));
                    }
                    else if (!voice.Source.isPlaying)
                    {
                        finished.Add((pair.Key, true));
                    }
                }

                foreach (var item in finished)
                {
                    if (!voices.TryGetValue(item.Id, out var voice))
                    {
                        continue;
                    }

                    var callback = item.Natural ? voice.Completed : null;
                    Stop(item.Id);
                    try
                    {
                        callback?.Invoke(new AudioHandle(this, item.Id));
                    }
                    catch (Exception error)
                    {
                        Logger.Exception(error);
                    }

                    if (IsDestroying)
                    {
                        break;
                    }
                }
            }
            finally
            {
                ticking = false;
            }
        }

        private bool AdvanceFade(Voice voice, float deltaTime)
        {
            var fade = voice.Fade;
            fade.Elapsed = Math.Min(fade.Duration, fade.Elapsed + deltaTime);
            float t = fade.Elapsed / fade.Duration;
            float factor = fade.Curve == AudioFadeCurve.SmoothStep ? t * t * (3 - 2 * t) : t;
            voice.Volume = fade.Curve == AudioFadeCurve.EqualPower ? (float)Math.Sqrt((1 - t) * fade.Start * fade.Start + t * fade.Target * fade.Target) : fade.Start + (fade.Target - fade.Start) * factor;
            if (fade.Elapsed >= fade.Duration)
            {
                voice.Volume = fade.Target;
                fade.Active = false;
            }

            voice.Fade = fade;
            ApplyVolume(voice);
            return !fade.Active && fade.StopAtEnd;
        }

        private static void ValidateFade(float seconds, AudioFadeCurve curve)
        {
            if (seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds))
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }

            if (!Enum.IsDefined(typeof(AudioFadeCurve), curve))
            {
                throw new ArgumentOutOfRangeException(nameof(curve));
            }
        }
    }
}
