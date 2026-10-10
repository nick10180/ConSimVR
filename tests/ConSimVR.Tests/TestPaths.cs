using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace ConSimVR.Tests
{
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class GameDirAttribute : Attribute
    {
        public GameDirAttribute(string path) => Path = path;
        public string Path { get; }
    }

    internal static class TestPaths
    {
        public static string GameDir =>
            typeof(TestPaths).Assembly.GetCustomAttribute<GameDirAttribute>()?.Path ?? "";

        public static string GameAssembly => Path.Combine(GameDir, "GameAssembly.dll");
    }

    /// <summary>A fact that only runs when the game is installed at the configured GameDir.</summary>
    public sealed class GameFactAttribute : FactAttribute
    {
        public GameFactAttribute()
        {
            if (!File.Exists(TestPaths.GameAssembly))
                Skip = $"Construction Simulator not found at '{TestPaths.GameDir}'.";
        }
    }
}
