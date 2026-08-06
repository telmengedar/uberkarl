<#
.SYNOPSIS
    Builds the C# solution and runs the headless in-engine behavior probe
    (game/Diagnostics/BehaviorHeadlessProbe.tscn), then reports its verdict.

.DESCRIPTION
    DiVoid #7747 (REOPENED) exposed a gap the project had no way to close before:
    the Godot-free `Uberkarl.Behavior`/`Uberkarl.Editor`/`Uberkarl.Content` unit
    tests, and even a real-Pooscript-executing test on a seam, can all pass while
    the actual running game does nothing -- because none of them ever drive a
    real Godot `SceneTree` with real physics (`CharacterBody2D.MoveAndSlide`
    resting a player a fraction of a pixel short of true geometric penetration
    against a solid tile, in this bug's case). This script is that missing
    verification layer: it builds the project the same way the editor does
    (`godot --headless --build-solutions`), then runs
    `game/Diagnostics/BehaviorHeadlessProbe.tscn` headless -- which plants a
    player on the real `content/sample.pkg` spike tile (cell 20,11) both by
    forced teleport (a sanity check that the dispatch/intent chain can fire at
    all) and by a real gravity-driven drop (the actual reproduction of "walk
    onto the spike"), through BOTH ways a level reaches
    `PlayRuntimeBuilder.Populate` (the editor-Play projection and the
    stand-alone `LevelLoader` path) -- and greps the `[probe] VERDICT`/`SUMMARY`
    lines out of the captured stdout.

    Deliberately NOT part of `dotnet test`: it needs the real Godot mono
    runtime (headless is fine, a Godot-less CI/dev-test run is not), so it is
    its own opt-in script rather than wired into any test project.

.PARAMETER GodotExe
    Path to the Godot 4 (.NET/Mono) editor executable. Defaults to this
    machine's known install; override for a different machine/CI runner.

.PARAMETER SkipBuild
    Skip the `--build-solutions` step (e.g. you just built and only want to
    re-run the probe against the existing build).

.EXAMPLE
    .\tools\run-behavior-probe.ps1

.EXAMPLE
    .\tools\run-behavior-probe.ps1 -GodotExe "C:\Games\Godot\Godot.exe" -SkipBuild
#>
[CmdletBinding()]
param(
    [string]$GodotExe = "C:\apps\godot\Godot_v4.7.1-stable_mono_windows_arm64\Godot_v4.7.1-stable_mono_windows_arm64.exe",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$probeScene = "res://game/Diagnostics/BehaviorHeadlessProbe.tscn"

if (-not (Test-Path $GodotExe)) {
    Write-Error "Godot executable not found at '$GodotExe'. Pass -GodotExe to point at your install."
    exit 2
}

# Run via Start-Process + file redirection rather than `& $exe ...` / `& $exe ... 2>&1`: on this
# project's Windows PowerShell 5.1, capturing a native Godot process through the call operator was
# observed to silently return zero output AND a blank $LASTEXITCODE (not just the documented
# NativeCommandError-wrapping gotcha from a redirected stderr) -- Start-Process -Wait -PassThru with
# -RedirectStandardOutput/-RedirectStandardError is the reliable way to get both the full stdout and
# a trustworthy exit code back from this executable.
$stdoutFile = [System.IO.Path]::GetTempFileName()
$stderrFile = [System.IO.Path]::GetTempFileName()

try {
    if (-not $SkipBuild) {
        Write-Host "==> Building C# solution (godot --headless --build-solutions)..." -ForegroundColor Cyan
        $buildArgs = @("--headless", "--path", $repoRoot, "--build-solutions", "--quit")
        $buildProc = Start-Process -FilePath $GodotExe -ArgumentList $buildArgs -NoNewWindow -Wait -PassThru `
            -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
        Get-Content $stdoutFile | ForEach-Object { Write-Host $_ }
        Get-Content $stderrFile | ForEach-Object { Write-Host $_ }
        if ($buildProc.ExitCode -ne 0) {
            Write-Error "Build failed (exit $($buildProc.ExitCode))."
            exit $buildProc.ExitCode
        }
    }

    Write-Host "==> Running headless behavior probe ($probeScene)..." -ForegroundColor Cyan
    $probeArgs = @("--headless", "--path", $repoRoot, $probeScene)
    $probeProc = Start-Process -FilePath $GodotExe -ArgumentList $probeArgs -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
    $probeExitCode = $probeProc.ExitCode
    $output = @(Get-Content $stdoutFile) + @(Get-Content $stderrFile)
}
finally {
    Remove-Item -Path $stdoutFile, $stderrFile -ErrorAction SilentlyContinue
}

$output | ForEach-Object { Write-Host $_ }

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
