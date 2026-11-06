namespace Game.Framework.Audio
{
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
        internal AudioHandle(AudioManager owner, long id)
        {
            this.owner = owner;
            Id = id;
        }

        public bool IsValid => owner != null && owner.Contains(Id);

        public void Stop() => owner?.Stop(Id);
        public void Pause() => owner?.Pause(Id, true);
        public void Resume() => owner?.Pause(Id, false);
        public void SetVolume(float volume) => owner?.SetVolume(Id, volume);
    }
}
