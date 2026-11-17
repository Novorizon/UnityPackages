using System;
namespace Game.Framework
{
    // Compatibility adapter: every log goes through the same configured backend.
    /// <summary>提供Net 日志接口的实现。</summary>
    public sealed class NetLogger : ILogger
    {
        /// <summary>调用 Logger.Info 完成信息。</summary>
        public void Info(string message) { Logger.Info(message); }
        /// <summary>调用 Logger.Warn 完成Warn。</summary>
        public void Warn(string message) { Logger.Warn(message); }
        /// <summary>调用 Logger.Error 完成错误。</summary>
        public void Error(string message) { Logger.Error(message); }
        /// <summary>调用 Logger.Exception 完成异常。</summary>
        public void Exception(Exception error) { Logger.Exception(error); }
    }
}
