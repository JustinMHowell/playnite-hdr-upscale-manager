using System;
using System.Collections.Generic;
using System.IO;
using HdrUpscaleManager.Services.Addons;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class RenoDxEngineDetectorTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "hdrum-tests-" + Guid.NewGuid());

        public RenoDxEngineDetectorTests() => Directory.CreateDirectory(root);

        public void Dispose()
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        [Fact]
        public void Detect_UnrealBinariesFolder_ReturnsUnrealEngineAndPath()
        {
            var binaries = Directory.CreateDirectory(Path.Combine(root, "Binaries", "Win64")).FullName;

            var result = RenoDxEngineDetector.Detect(root, "Some Game");

            Assert.Equal(RenoDxEngine.UnrealEngine, result.Engine);
            Assert.Equal(binaries, result.InstallPath);
        }

        [Fact]
        public void Detect_UnrealBinariesUnderEngineFolder_IsIgnored()
        {
            // The engine's own shared Binaries\Win64 folder must not be mistaken for the game's.
            Directory.CreateDirectory(Path.Combine(root, "Engine", "Binaries", "Win64"));

            var result = RenoDxEngineDetector.Detect(root, "Some Game");

            Assert.Null(result.Engine);
        }

        [Fact]
        public void Detect_UnityPlayerDll_ReturnsUnityEngineAndPath()
        {
            File.WriteAllBytes(Path.Combine(root, "UnityPlayer.dll"), new byte[] { 0 });

            var result = RenoDxEngineDetector.Detect(root, "Some Game");

            Assert.Equal(RenoDxEngine.UnityEngine, result.Engine);
            Assert.Equal(root, result.InstallPath);
        }

        [Fact]
        public void Detect_NoMarkers_ReturnsNullEngineAndOriginalRoot()
        {
            var result = RenoDxEngineDetector.Detect(root, "Some Game");

            Assert.Null(result.Engine);
            Assert.Equal(root, result.InstallPath);
        }

        [Fact]
        public void Detect_EngineHintOverride_SkipsAutoDetection()
        {
            File.WriteAllBytes(Path.Combine(root, "UnityPlayer.dll"), new byte[] { 0 });
            var hints = new Dictionary<string, string> { ["Some Game"] = "Unreal Engine" };

            var result = RenoDxEngineDetector.Detect(root, "Some Game", engineHintOverrides: hints);

            Assert.Equal(RenoDxEngine.UnrealEngine, result.Engine);
        }

        [Fact]
        public void Detect_InstallPathOverride_ScansOverriddenSubfolder()
        {
            var subGame = Directory.CreateDirectory(Path.Combine(root, "GamePass", "Content")).FullName;
            Directory.CreateDirectory(Path.Combine(subGame, "Binaries", "Win64"));
            var overrides = new Dictionary<string, string> { ["Some Game"] = @"GamePass\Content" };

            var result = RenoDxEngineDetector.Detect(root, "Some Game", installPathOverrides: overrides);

            Assert.Equal(RenoDxEngine.UnrealEngine, result.Engine);
            Assert.StartsWith(subGame, result.InstallPath);
        }
    }
}
