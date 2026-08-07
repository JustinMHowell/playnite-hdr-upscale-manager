<#
.SYNOPSIS
  Run the local FlaUI UI-automation tests with detailed live output.

.DESCRIPTION
  Thin wrapper around `dotnet test` so re-running (or filtering to one test while iterating
  on a selector) is a single command instead of re-typing the full invocation each time.

.PARAMETER Filter
  xunit test filter, e.g. "ManifestSyncUiTests" or a full test name. Omit to run everything
  in this project.

.PARAMETER PlayniteExe
  Optional override for HUM_UITEST_PLAYNITE_EXE. Usually unnecessary — PlayniteFixture already
  sniffs the path from a running process, then the registry, then %LOCALAPPDATA%.

.EXAMPLE
  .\run-tests.ps1
  .\run-tests.ps1 -Filter ManifestSyncUiTests
  .\run-tests.ps1 -Filter ManagerWindowUiTests -PlayniteExe "D:\Playnite\Playnite\Playnite.DesktopApp.exe"
#>
[CmdletBinding()]
param(
    [string]$Filter,
    [string]$PlayniteExe
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

if ($PlayniteExe) {
    $env:HUM_UITEST_PLAYNITE_EXE = $PlayniteExe
}

$testArgs = @('test', $root, '--logger', 'console;verbosity=detailed')
if ($Filter) {
    $testArgs += @('--filter', $Filter)
}

& dotnet @testArgs
exit $LASTEXITCODE
