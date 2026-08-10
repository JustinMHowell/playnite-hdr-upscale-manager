<#
.SYNOPSIS
  Single entry point for running this repo's test suites.

.DESCRIPTION
  Always fully clears the relevant obj/bin folders first, then builds
  whatever the chosen suite needs, then runs dotnet test with the given
  filter. The clean step exists because mixing dotnet CLI builds (dotnet
  test, including its own build of the main project via
  HdrUpscaleManager.Tests' ProjectReference) with MSBuild.exe builds
  (build-and-run.ps1, needed to deploy the addon for UI tests) on the same
  non-SDK-style WPF net462 project's obj folder leaves it in a state where
  the next MSBuild.exe build silently drops the WPF markup-compile output
  (.g.resources) from the DLL — confirmed live twice on 2026-08-08, both
  times surfacing as "Cannot locate resource 'ui/managerview.xaml'" at
  runtime. A full clean before every run side-steps that regardless of what
  ran last, at the cost of a slower build.

.PARAMETER Suite
  "Unit" (default) — HdrUpscaleManager.Tests via dotnet test only, no deploy.
  "Ui" — deploys the addon via build-and-run.ps1 -NoRestart (MSBuild.exe)
  first, then runs HdrUpscaleManager.UiTests against the deployed copy.
  These tests drive a real Playnite instance and mutate real game
  installs/state — see HdrUpscaleManager.UiTests/README.md before running.

.PARAMETER Filter
  Optional xunit --filter expression, passed straight through to dotnet test.

.EXAMPLE
  .\run-tests.ps1
  .\run-tests.ps1 -Suite Unit -Filter "FullyQualifiedName~PeInspector"
  .\run-tests.ps1 -Suite Ui -Filter "FullyQualifiedName~DxvkInstallUiTests"
#>
[CmdletBinding()]
param(
    [ValidateSet("Unit", "Ui")]
    [string]$Suite = "Unit",

    [string]$Filter
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Remove-Item -Recurse -Force "$root\obj", "$root\bin" -ErrorAction SilentlyContinue

if ($Suite -eq "Ui") {
    Remove-Item -Recurse -Force "$root\HdrUpscaleManager.UiTests\obj", "$root\HdrUpscaleManager.UiTests\bin" -ErrorAction SilentlyContinue
    & "$root\build-and-run.ps1" -NoRestart
    $project = "$root\HdrUpscaleManager.UiTests"
}
else {
    Remove-Item -Recurse -Force "$root\HdrUpscaleManager.Tests\obj", "$root\HdrUpscaleManager.Tests\bin" -ErrorAction SilentlyContinue
    $project = "$root\HdrUpscaleManager.Tests"
}

$testArgs = @("test", $project, "-c", "Debug", "--logger", "console;verbosity=detailed")
if ($Filter) { $testArgs += @("--filter", $Filter) }

dotnet @testArgs
