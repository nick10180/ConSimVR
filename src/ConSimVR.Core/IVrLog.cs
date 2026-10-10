using System;

namespace ConSimVR.Core
{
    /// <summary>Logging sink so core code stays free of BepInEx.</summary>
    public interface IVrLog
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
    }

    /// <summary>Writes to the console; used by tests and as a fallback.</summary>
    public sealed class ConsoleVrLog : IVrLog
    {
        public void Info(string message) => Console.WriteLine("[Info] " + message);
        public void Warn(string message) => Console.WriteLine("[Warn] " + message);
        public void Error(string message) => Console.WriteLine("[Error] " + message);
    }
}
