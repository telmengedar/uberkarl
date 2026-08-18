<#
.SYNOPSIS
    Builds the C# solution and runs the headless in-engine behavior probe
    (game/Diagnostics/BehaviorHeadlessProbe.tscn), then reports its verdict.

.DESCRIPTION
    Three things keep this fast and visible, each of which replaced a slower or
    quieter approach (DiVoid #8296):

    * The build is a plain `dotnet build`. The project is an ordinary
      Godot.NET.Sdk project, so `godot --build-solutions` is not needed -- and it
      costs minutes, because it boots an entire editor (filesystem scan, plugin
      init, class registration, layout load) to run a two-second compile.
    * Godot is invoked through its `_console.exe` variant. The plain exe is
      GUI-subsystem, so PowerShell neither waits for it nor receives its output;
      the console variant streams live and returns a real exit code.
    * `--fixed-fps` disables real-time synchronisation, so the probe's ~6400
      physics frames run as fast as the CPU allows instead of at wall-clock 60fps.
      The delta stays 1/60, so results are identical -- verified bit-for-bit.

.PARAMETER GodotExe
    Path to the Godot 4 (.NET/Mono) editor executable. The `_console.exe` sibling
    is preferred automatically when it exists.

.PARAMETER FixedFps
    Physics/main-loop rate for the probe run. Keep at 60 to match the project's
    physics tick. Pass 0 to run at wall-clock speed (~2 minutes) if you are
    chasing something timing-sensitive.

.PARAMETER SkipBuild
    Skip the build step.

.EXAMPLE
    .\tools\run-behavior-probe.ps1

.EXAMPLE
    .\tools\run-behavior-probe.ps1 -SkipBuild -FixedFps 0
#>
[CmdletBinding()]
param(
    [string]$GodotExe = "C:\apps\godot\Godot_v4.7.1-stable_mono_windows_arm64\Godot_v4.7.1-stable_mono_windows_arm64.exe",
    [int]$FixedFps = 60,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$probeScene = "res://game/Diagnostics/BehaviorHeadlessProbe.tscn"

if (-not (Test-Path $GodotExe)) {
    Write-Error "Godot executable not found at '$GodotExe'. Pass -GodotExe to point at your install."
    exit 2
}

# The plain Godot exe is GUI-subsystem: PowerShell does not wait for it and never sees its
# stdout. The _console.exe sibling is console-subsystem and behaves like a normal CLI tool.
$consoleExe = [System.IO.Path]::ChangeExtension($GodotExe, $null).TrimEnd('.') + "_console.exe"
if (Test-Path $consoleExe) {
    $GodotExe = $consoleExe
} else {
    Write-Warning "No _console.exe next to '$GodotExe' -- output may not stream and the exit code may be unreliable."
}

if (-not $SkipBuild) {
    Write-Host "==> Building (dotnet build)..." -ForegroundColor Cyan
    # -nodeReuse:false: MSBuild worker nodes otherwise linger for ~15 minutes after the build,
    # which is what made the old godot --build-solutions step appear to hang.
    dotnet build (Join-Path $repoRoot "Uberkarl.csproj") -nodeReuse:false -v minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed (exit $LASTEXITCODE)."
        exit $LASTEXITCODE
    }
}

Write-Host "==> Running headless behavior probe ($probeScene)..." -ForegroundColor Cyan
$probeArgs = @("--headless", "--path", $repoRoot)
if ($FixedFps -gt 0) {
    $probeArgs += @("--fixed-fps", "$FixedFps")
}
$probeArgs += $probeScene

# Tee-Object streams to the console AND captures, so a long run is visible while it happens.
& $GodotExe @probeArgs | Tee-Object -Variable output
$probeExitCode = $LASTEXITCODE

Write-Host ""
Write-Host "==> Verdict lines:" -ForegroundColor Cyan
$verdictLines = $output | Select-String -Pattern '\[probe\] (VERDICT|SUMMARY)'
if ($verdictLines) {
    $verdictLines | ForEach-Object { Write-Host $_.Line -ForegroundColor Yellow }
} else {
    Write-Warning "No '[probe] VERDICT'/'[probe] SUMMARY' lines found in the probe's output -- it likely crashed before reaching them. Check the full output above."
}

Write-Host ""
if ($probeExitCode -eq 0) {
    Write-Host "==> Probe exit code 0: the path Toni actually plays (editor-Play projection, real physics) took damage from the spike." -ForegroundColor Green
} else {
    Write-Host "==> Probe exit code ${probeExitCode}: the path Toni actually plays did NOT take damage -- behavior regression reproduced in-engine." -ForegroundColor Red
}

exit $probeExitCode
