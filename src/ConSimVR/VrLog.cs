using System.Collections.Concurrent;
using System.Threading;
using BepInEx.Logging;
using ConSimVR.Core;

namespace ConSimVR
{
    /// <summary>
    /// IVrLog over BepInEx. Messages from other threads (Unity's render thread runs our OpenXR submission) are queued
    /// and written from the main thread, so nothing on the render thread touches IL2CPP or Unity logging.
    /// </summary>
    internal sealed class VrLog : IVrLog
    {
        private readonly ManualLogSource _source;
        private readonly int _mainThreadId;
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _pending = new();

        public VrLog(ManualLogSource source)
        {
            _source = source;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public void Info(string message) => Write(LogLevel.Info, message);
        public void Warn(string message) => Write(LogLevel.Warning, message);
        public void Error(string message) => Write(LogLevel.Error, message);

        private void Write(LogLevel level, string message)
        {
            if (Thread.CurrentThread.ManagedThreadId == _mainThreadId)
                _source.Log(level, message);
            else if (_pending.Count < 256)
                _pending.Enqueue((level, "[render thread] " + message));
        }

        /// <summary>Writes queued messages; call from the main thread.</summary>
        public void Drain()
        {
            while (_pending.TryDequeue(out var entry))
                _source.Log(entry.Level, entry.Message);
        }
    }
}
