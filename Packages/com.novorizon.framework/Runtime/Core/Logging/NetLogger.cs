using System;
namespace Game.Framework
{
    // Compatibility adapter: every log goes through the same configured backend.
    public sealed class NetLogger : ILogger
    {
        public void Info(string message) { Logger.Info(message); }
        public void Warn(string message) { Logger.Warn(message); }
        public void Error(string message) { Logger.Error(message); }
        public void Exception(Exception error) { Logger.Exception(error); }
    }
}
