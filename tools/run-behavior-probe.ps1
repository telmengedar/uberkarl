<#
.SYNOPSIS
    Builds the C# solution and runs the headless in-engine behavior probe
    (game/Diagnostics/BehaviorHeadlessProbe.tscn), then reports its verdict.

.PARAMETER GodotExe
    Path to the Godot 4 (.NET/Mono) editor executable.

.PARAMETER SkipBuild
    Skip the `--build-solutions` step.

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

# Windows PowerShell 5.1: `& $exe ...` silently drops native-process stdout and $LASTEXITCODE.
# Start-Process with redirected files is the reliable way to get both back.
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
