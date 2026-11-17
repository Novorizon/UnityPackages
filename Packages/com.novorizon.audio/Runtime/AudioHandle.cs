namespace Game.Framework.Audio
{
    /// <summary>定义音频分组可选的枚举值。</summary>
    public enum AudioGroup
    {
        Music,
        Sound,
        Voice,
        UI
    }

    /// <summary>一次播放的控制句柄；声音结束或回收后，旧句柄不会影响复用该音源的新播放。</summary>
    public readonly struct AudioHandle
    {
        private readonly AudioManager owner;
        internal readonly long Id;
        // 将句柄绑定到管理器与本次播放 ID。
        internal AudioHandle(AudioManager owner, long id)
        {
            this.owner = owner;
            Id = id;
        }

        /// <summary>判断句柄指向的播放是否仍由管理器持有。</summary>
        public bool IsValid => owner != null && owner.Contains(Id);

        /// <summary>主动停止当前播放。默认句柄或失效句柄可以安全调用。</summary>
        public void Stop() => owner?.Stop(Id);
        /// <summary>暂停当前播放。</summary>
        public void Pause() => owner?.Pause(Id, true);
        /// <summary>解除当前播放的暂停，仍受管理器全局暂停状态影响。</summary>
        public void Resume() => owner?.Pause(Id, false);
        /// <summary>立即设置当前播放音量，取消已有渐变。</summary>
        public void SetVolume(float volume) => owner?.SetVolume(Id, volume);
        /// <summary>在指定秒数内渐变到目标音量，句柄无效时返回 false。</summary>
        public bool FadeTo(float volume, float seconds, AudioFadeCurve curve = AudioFadeCurve.Linear) => owner != null && owner.FadeTo(Id, volume, seconds, false, curve);
        /// <summary>渐变至静音并结束播放，句柄无效时返回 false。</summary>
        public bool FadeOut(float seconds, AudioFadeCurve curve = AudioFadeCurve.Linear) => owner != null && owner.FadeTo(Id, 0, seconds, true, curve);
    }
}
