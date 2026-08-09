using System;
using System.Collections.Generic;
using System.IO;
using HdrUpscaleManager.Models;
using HdrUpscaleManager.Models.Addons;
using HdrUpscaleManager.Services.Addons;
using HdrUpscaleManager.Services.ReShade;
using Xunit;

namespace HdrUpscaleManager.Tests
{
    public class OptiScalerReShadeCoexistenceTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "hdrum-tests-" + Guid.NewGuid());

        public OptiScalerReShadeCoexistenceTests()
        {
            Directory.CreateDirectory(directory);

            // ReShadeInstallService.IsInstalled always calls PeInspector.FindGameExecutable first,
            // even when an apiOverride makes the actual PE inspection unnecessary — it just needs
            // *an* .exe to exist. Every test in this class supplies apiOverride explicitly.
            WriteFile("game.exe", "exe");
        }

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        private string WriteFile(string name, string content = "content") =>
            WriteFileAt(directory, name, content);

        private static string WriteFileAt(string dir, string name, string content)
        {
            var path = Path.Combine(dir, name);
            File.WriteAllText(path, content);
            return path;
        }

        // ── OptiScalerService.IsInstalled ───────────────────────────────────────

        [Fact]
        public void OptiScalerIsInstalled_IniPresent_ReturnsTrue()
        {
            WriteFile("OptiScaler.ini");

            Assert.True(OptiScalerService.IsInstalled(directory));
        }

        [Fact]
        public void OptiScalerIsInstalled_IniAbsent_ReturnsFalse()
        {
            Assert.False(OptiScalerService.IsInstalled(directory));
        }

        // ── ReShadeInstallService.IsInstalled (coexistence-aware) ───────────────

        [Fact]
        public void ReShadeIsInstalled_NormalProxyDllPresent_ReturnsTrue()
        {
            WriteFile("dxgi.dll");

            Assert.True(ReShadeInstallService.IsInstalled(directory, GraphicsApi.D3D11));
        }

        [Fact]
        public void ReShadeIsInstalled_OnlyCoexistNamePresent_StillReturnsTrue()
        {
            WriteFile(OptiScalerService.ReShadeCoexistName);

            Assert.True(ReShadeInstallService.IsInstalled(directory, GraphicsApi.D3D11));
        }

        [Fact]
        public void ReShadeIsInstalled_NeitherFilePresent_ReturnsFalse()
        {
            Assert.False(ReShadeInstallService.IsInstalled(directory, GraphicsApi.D3D11));
        }

        [Fact]
        public void ReShadeIsInstalled_OptiScalerPresentButOnlyItsOwnDxgiDllExists_ReturnsFalse()
        {
            // Regression for the bug confirmed live 2026-08-09 on Deep Rock Galactic:
            // EnsureReShadeInstalledForRenoDxAsync thought ReShade was already installed because
            // OptiScaler's own dxgi.dll satisfied the naive proxy-name existence check, so it
            // never actually installed ReShade — RenoDX's addon was left with no real host.
            // Once OptiScaler is confirmed present, a file at the plain proxy name is its own,
            // not ReShade's — only ReShadeCoexistName can mean ReShade at that point.
            WriteFile("OptiScaler.ini");
            WriteFile("dxgi.dll"); // OptiScaler's own file, not ReShade's

            Assert.False(ReShadeInstallService.IsInstalled(directory, GraphicsApi.D3D11));
        }

        [Fact]
        public void ReShadeIsInstalled_OptiScalerPresentAndCoexistNameExists_ReturnsTrue()
        {
            WriteFile("OptiScaler.ini");
            WriteFile("dxgi.dll"); // OptiScaler's own file
            WriteFile(OptiScalerService.ReShadeCoexistName); // ReShade, correctly displaced

            Assert.True(ReShadeInstallService.IsInstalled(directory, GraphicsApi.D3D11));
        }

        // ── Scenario: OptiScaler already installed, then the user installs ReShade ──

        [Fact]
        public void ResolveTargetDllName_OptiScalerAlreadyInstalled_RedirectsToCoexistName()
        {
            WriteFile("OptiScaler.ini");

            var target = ReShadeInstallService.ResolveTargetDllName(directory, "dxgi.dll");

            Assert.Equal(OptiScalerService.ReShadeCoexistName, target);
        }

        [Fact]
        public void ResolveTargetDllName_OptiScalerNotInstalled_KeepsNormalProxyName()
        {
            var target = ReShadeInstallService.ResolveTargetDllName(directory, "dxgi.dll");

            Assert.Equal("dxgi.dll", target);
        }

        // ── Scenario: ReShade already installed, then the user installs OptiScaler ──

        [Fact]
        public void ShouldDisplaceReShade_ReShadeOccupiesOptiScalersProxyName_ReturnsTrue()
        {
            WriteFile("dxgi.dll");

            Assert.True(OptiScalerService.ShouldDisplaceReShade(directory, "dxgi.dll", GraphicsApi.D3D11));
        }

        [Fact]
        public void ShouldDisplaceReShade_NoReShadePresent_ReturnsFalse()
        {
            Assert.False(OptiScalerService.ShouldDisplaceReShade(directory, "dxgi.dll", GraphicsApi.D3D11));
        }

        [Fact]
        public void ShouldDisplaceReShade_VulkanHasNoDropInProxy_ReturnsFalse()
        {
            // Vulkan games have no ReShade drop-in proxy at all (see ReShadeProxyDll), so
            // OptiScaler's own winmm.dll pick can never collide with a ReShade install.
            WriteFile("winmm.dll");

            Assert.False(OptiScalerService.ShouldDisplaceReShade(directory, "winmm.dll", GraphicsApi.Vulkan));
        }

        [Fact]
        public void MoveReShadeToCoexistName_ExistingReShadeFile_MovesItAside()
        {
            WriteFile("dxgi.dll", "reshade bytes");

            OptiScalerService.MoveReShadeToCoexistName(directory, "dxgi.dll");

            Assert.False(File.Exists(Path.Combine(directory, "dxgi.dll")));
            var coexistPath = Path.Combine(directory, OptiScalerService.ReShadeCoexistName);
            Assert.True(File.Exists(coexistPath));
            Assert.Equal("reshade bytes", File.ReadAllText(coexistPath));
        }

        [Fact]
        public void MoveReShadeToCoexistName_NoFileAtSlot_DoesNothing()
        {
            OptiScalerService.MoveReShadeToCoexistName(directory, "dxgi.dll");

            Assert.False(File.Exists(Path.Combine(directory, OptiScalerService.ReShadeCoexistName)));
        }

        [Fact]
        public void MoveReShadeToCoexistName_CoexistNameAlreadyOccupied_LeavesBothFilesInPlace()
        {
            WriteFile("dxgi.dll", "reshade bytes");
            WriteFile(OptiScalerService.ReShadeCoexistName, "stale leftover");

            OptiScalerService.MoveReShadeToCoexistName(directory, "dxgi.dll");

            Assert.True(File.Exists(Path.Combine(directory, "dxgi.dll")));
            Assert.Equal("stale leftover", File.ReadAllText(Path.Combine(directory, OptiScalerService.ReShadeCoexistName)));
        }

        // ── RestoreReShadeFromCoexistName (OptiScaler uninstall) ────────────────

        [Fact]
        public void RestoreReShadeFromCoexistName_CoexistFileExists_MovesBackToRestoreName()
        {
            WriteFile(OptiScalerService.ReShadeCoexistName, "reshade bytes");

            OptiScalerService.RestoreReShadeFromCoexistName(directory, "dxgi.dll");

            Assert.False(File.Exists(Path.Combine(directory, OptiScalerService.ReShadeCoexistName)));
            var restoredPath = Path.Combine(directory, "dxgi.dll");
            Assert.True(File.Exists(restoredPath));
            Assert.Equal("reshade bytes", File.ReadAllText(restoredPath));
        }

        [Fact]
        public void RestoreReShadeFromCoexistName_NoCoexistFile_DoesNothing()
        {
            OptiScalerService.RestoreReShadeFromCoexistName(directory, "dxgi.dll");

            Assert.False(File.Exists(Path.Combine(directory, "dxgi.dll")));
        }

        [Fact]
        public void RestoreReShadeFromCoexistName_RestoreTargetOccupied_LeavesReShadeAtCoexistName()
        {
            WriteFile(OptiScalerService.ReShadeCoexistName, "reshade bytes");
            WriteFile("dxgi.dll", "something else already claimed this name");

            OptiScalerService.RestoreReShadeFromCoexistName(directory, "dxgi.dll");

            var coexistPath = Path.Combine(directory, OptiScalerService.ReShadeCoexistName);
            Assert.True(File.Exists(coexistPath));
            Assert.Equal("reshade bytes", File.ReadAllText(coexistPath));
            Assert.Equal("something else already claimed this name", File.ReadAllText(Path.Combine(directory, "dxgi.dll")));
        }

        // ── End-to-end: OptiScalerService.Uninstall restores ReShade regardless of
        //    which install order produced the coexisting ReShade64.dll ──────────────

        [Fact]
        public void Uninstall_ReShadeCoexisting_RestoresReShadeToOptiScalersVacatedSlot()
        {
            // On disk this looks identical whether OptiScaler was installed first (and moved
            // ReShade aside) or ReShade was installed second (and deployed straight to the
            // coexist name) — both converge to: OptiScaler owns "dxgi.dll", ReShade sits at
            // ReShadeCoexistName. Uninstalling OptiScaler must hand "dxgi.dll" back to ReShade.
            WriteFile("dxgi.dll", "optiscaler bytes");
            WriteFile("OptiScaler.ini", "optiscaler ini");
            WriteFile(OptiScalerService.ReShadeCoexistName, "reshade bytes");

            var record = new OptiScalerInstalledRecord
            {
                GameId = "game-1",
                GameName = "Some Game",
                InstallPath = directory,
                DllName = "dxgi.dll",
                InstalledFiles = new List<string> { "dxgi.dll", "OptiScaler.ini" },
            };

            var result = OptiScalerService.Uninstall(record);

            Assert.True(result.Success);
            Assert.False(File.Exists(Path.Combine(directory, "OptiScaler.ini")));
            Assert.False(File.Exists(Path.Combine(directory, OptiScalerService.ReShadeCoexistName)));
            var reShadePath = Path.Combine(directory, "dxgi.dll");
            Assert.True(File.Exists(reShadePath));
            Assert.Equal("reshade bytes", File.ReadAllText(reShadePath));
        }

        [Fact]
        public void Uninstall_NoReShadeCoexisting_LeavesSlotEmptyAfterRemovingOwnFile()
        {
            WriteFile("dxgi.dll", "optiscaler bytes");
            WriteFile("OptiScaler.ini", "optiscaler ini");

            var record = new OptiScalerInstalledRecord
            {
                GameId = "game-1",
                GameName = "Some Game",
                InstallPath = directory,
                DllName = "dxgi.dll",
                InstalledFiles = new List<string> { "dxgi.dll", "OptiScaler.ini" },
            };

            var result = OptiScalerService.Uninstall(record);

            Assert.True(result.Success);
            Assert.False(File.Exists(Path.Combine(directory, "dxgi.dll")));
        }

        // ── ReShadeInstallService.Uninstall ─────────────────────────────────────

        [Fact]
        public void ReShadeUninstall_ProxyDllPresent_RemovesIt()
        {
            WriteFile("dxgi.dll", "reshade bytes");

            var result = ReShadeInstallService.Uninstall(directory, GraphicsApi.D3D11);

            Assert.True(result.Success);
            Assert.False(File.Exists(Path.Combine(directory, "dxgi.dll")));
        }

        [Fact]
        public void ReShadeUninstall_BackupExists_RestoresOriginalFile()
        {
            WriteFile("dxgi.dll", "reshade bytes");
            WriteFile("dxgi.dll.original", "the game's real dxgi.dll");

            var result = ReShadeInstallService.Uninstall(directory, GraphicsApi.D3D11);

            Assert.True(result.Success);
            var restoredPath = Path.Combine(directory, "dxgi.dll");
            Assert.True(File.Exists(restoredPath));
            Assert.Equal("the game's real dxgi.dll", File.ReadAllText(restoredPath));
            Assert.False(File.Exists(restoredPath + ".original"));
        }

        [Fact]
        public void ReShadeUninstall_NotInstalled_ReturnsFailure()
        {
            var result = ReShadeInstallService.Uninstall(directory, GraphicsApi.D3D11);

            Assert.False(result.Success);
        }

        [Fact]
        public void ReShadeUninstall_OptiScalerPresent_RefusesAndLeavesFilesAlone()
        {
            // OptiScaler owns "dxgi.dll" for itself once installed, and hosts ReShade under
            // ReShadeCoexistName instead — removing either file here without going through
            // OptiScaler's own Uninstall would desync OptiScaler's InstalledFiles record from
            // what's actually on disk. Same reasoning as ResolveTargetDllName/IsInstalled above.
            WriteFile("OptiScaler.ini");
            WriteFile("dxgi.dll", "optiscaler bytes");
            WriteFile(OptiScalerService.ReShadeCoexistName, "reshade bytes");

            var result = ReShadeInstallService.Uninstall(directory, GraphicsApi.D3D11);

            Assert.False(result.Success);
            Assert.True(File.Exists(Path.Combine(directory, "dxgi.dll")));
            Assert.True(File.Exists(Path.Combine(directory, OptiScalerService.ReShadeCoexistName)));
        }
    }
}
