namespace Game.Framework.Samples.Demo
{
    // The lazy singleton owns only this demo counter. It is not a player/account service.
    public sealed class DemoSession : Singleton<DemoSession>
    {
        public int Visits { get; private set; }
        public void RegisterVisit() { Visits++; }
    }
}
