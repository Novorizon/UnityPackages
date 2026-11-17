using System;
using System.Diagnostics;
using System.Threading;

namespace Game.Framework
{
    /// <summary>定义日志接口的调用契约。</summary>
    public interface ILogger
    {
        /// <summary>处理信息。</summary>
        void Info(string message);
        /// <summary>处理Warn。</summary>
        void Warn(string message);
        /// <summary>处理错误。</summary>
        void Error(string message);
        /// <summary>处理异常。</summary>
        void Exception(Exception exception);
    }
    /// <summary>提供日志接口的实现。</summary>
    public static class Logger
    {
        /// <summary>表示日志接口中的Console 日志接口。</summary>
        private sealed class ConsoleLogger : ILogger
        {
            /// <summary>调用 Console.WriteLine 完成信息。</summary>
            public void Info(string message) { Console.WriteLine(message); }
            /// <summary>调用 Console.Error.WriteLine 完成Warn。</summary>
            public void Warn(string message) { Console.Error.WriteLine(message); }
            /// <summary>调用 Console.Error.WriteLine 完成错误。</summary>
            public void Error(string message) { Console.Error.WriteLine(message); }
            /// <summary>调用 Console.Error.WriteLine 完成异常。</summary>
            public void Exception(Exception error) { Console.Error.WriteLine(error); }
        }
        private static ILogger sink = new ConsoleLogger();
        public static ILogger Sink { get => Volatile.Read(ref sink); set => Interlocked.Exchange(ref sink, value ?? throw new ArgumentNullException(nameof(value))); }
        /// <summary>调用 Sink.Info 完成信息。</summary>
        [Conditional("ENABLE_DEBUG_LOG")] public static void Info(string message) { Sink.Info(message); }
        /// <summary>调用 Sink.Warn 完成Warn。</summary>
        public static void Warn(string message) { Sink.Warn(message); }
        /// <summary>调用 Sink.Error 完成错误。</summary>
        public static void Error(string message) { Sink.Error(message); }
        /// <summary>调用 Sink.Exception 完成异常。</summary>
        public static void Exception(Exception error) { Sink.Exception(error); }
    }
}
