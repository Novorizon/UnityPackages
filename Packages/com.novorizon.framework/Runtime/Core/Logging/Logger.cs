using System;
using System.Diagnostics;
using System.Threading;

namespace Game.Framework
{
    public interface ILogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
        void Exception(Exception exception);
    }
    public static class Logger
    {
        private sealed class ConsoleLogger : ILogger
        {
            public void Info(string message) { Console.WriteLine(message); }
            public void Warn(string message) { Console.Error.WriteLine(message); }
            public void Error(string message) { Console.Error.WriteLine(message); }
            public void Exception(Exception error) { Console.Error.WriteLine(error); }
        }
        private static ILogger sink = new ConsoleLogger();
        public static ILogger Sink { get => Volatile.Read(ref sink); set => Interlocked.Exchange(ref sink, value ?? throw new ArgumentNullException(nameof(value))); }
        [Conditional("ENABLE_DEBUG_LOG")] public static void Info(string message) { Sink.Info(message); }
        public static void Warn(string message) { Sink.Warn(message); }
        public static void Error(string message) { Sink.Error(message); }
        public static void Exception(Exception error) { Sink.Exception(error); }
    }
}
