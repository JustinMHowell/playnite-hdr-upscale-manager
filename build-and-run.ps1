<#
.SYNOPSIS
  Build the extension in Release and deploy it into the local Playnite install
  for testing. Mirrors what .github/workflows/build.yaml does, minus packaging.

.DESCRIPTION
  1. Closes Playnite if it's running (its extension DLLs are locked while open,
     which would make the deploy step below fail).
  2. Locates MSBuild via vswhere (no PATH setup needed).
  3. Restores + builds HdrUpscaleManager.csproj in Release (net462, WPF).
  4. Copies the output into %AppData%\Playnite\Extensions\hdr-upscale-manager\:
       - every *.dll EXCEPT Playnite.SDK.dll (reference-only; the Playnite
         process supplies its own at runtime — shipping ours risks a load conflict)
       - extension.yaml
       - Resources\ (icon.png, etc.)
  5. Relaunches Playnite (standalone, %LOCALAPPDATA%\Playnite\Playnite.DesktopApp.exe).

.PARAMETER NoRestart
  Skip closing/relaunching Playnite — just build and deploy.

.EXAMPLE
  .\build-and-run.ps1
  .\build-and-run.ps1 -NoRestart
#>
[CmdletBinding()]
param(
    [switch]$NoRestart
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$playniteExe = Join-Path $env:LOCALAPPDATA 'Playnite\Playnite.DesktopApp.exe'

# --- Close Playnite (its extension DLLs are locked while running) ---------
$playniteProc = Get-Process Playnite* -ErrorAction SilentlyContinue
if ($playniteProc -and -not $NoRestart) {
    Write-Host "Stopping Playnite..." -ForegroundColor DarkGray
    $playniteProc | Stop-Process -Force
    $playniteProc | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
}

# --- Locate MSBuild -------------------------------------------------------
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found — is Visual Studio / Build Tools installed?" }

# Prefers VS 2022 (17.x): VS 18 Build Tools' SDK resolver can't resolve
# Microsoft.NET.Sdk against the .NET 9 SDKs installed here (confirmed 2026-09-30,
# MSB4236), while 2022's resolves them fine. Falls back to the newest install.
$msbuild = & $vswhere -version '[17.0,18.0)' -products '*' `
    -requires Microsoft.Component.MSBuild `
    -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) {
    $msbuild = & $vswhere -latest -products '*' `
        -requires Microsoft.Component.MSBuild `
        -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}
if (-not $msbuild) { throw "MSBuild.exe not found via vswhere." }
Write-Host "MSBuild: $msbuild" -ForegroundColor DarkGray

# --- Build ----------------------------------------------------------------
& $msbuild "$root\HdrUpscaleManager.csproj" /t:Restore,Build /p:Configuration=Release /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }

# --- Deploy ---------------------------------------------------------------
$bin   = Join-Path $root 'bin\Release\net462'
$dest  = Join-Path $env:APPDATA 'Playnite\Extensions\hdr-upscale-manager'
New-Item -ItemType Directory -Path $dest -Force | Out-Null

Get-ChildItem "$bin\*.dll" | Where-Object { $_.Name -ne 'Playnite.SDK.dll' } |
    Copy-Item -Destination $dest -Force
Copy-Item "$bin\extension.yaml" $dest -Force
if (Test-Path "$root\Resources") {
    Copy-Item "$root\Resources" $dest -Recurse -Force
}

Write-Host "Deployed to $dest" -ForegroundColor Green

# --- Relaunch Playnite ------------------------------------------------------
if (-not $NoRestart) {
    Write-Host "Launching Playnite ($playniteExe)..." -ForegroundColor DarkGray
    Start-Process $playniteExe
}
