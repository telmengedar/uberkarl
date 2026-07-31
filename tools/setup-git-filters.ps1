<#
.SYNOPSIS
    One-time git filter setup for this clone of Uberkarl.

.DESCRIPTION
    Registers the `godotstripmcp` clean filter (declared in .gitattributes
    as `project.godot filter=godotstripmcp`) that strips the godot_mcp
    dev-addon footprint -- its [autoload] singletons and its entry in
    editor_plugins/enabled -- out of project.godot before it is written to
    a commit. See tools/strip-mcp-refs.py for the stripping logic.

    Git deliberately does NOT run filter.* config automatically on `git
    clone`/`git init` -- a committed .gitattributes could otherwise
    reference an arbitrary local command and get it executed just by
    cloning. filter.* entries live in local, non-versioned git config
    (.git/config), so every fresh clone (or brand-new `git init`) needs
    this run once before its first commit that touches project.godot.

    This is equivalent to running, from the repo root:
        git config filter.godotstripmcp.clean "python tools/strip-mcp-refs.py"
        git config filter.godotstripmcp.smudge cat

.EXAMPLE
    .\tools\setup-git-filters.ps1

.EXAMPLE
    pwsh -File tools/setup-git-filters.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = git rev-parse --show-toplevel 2>$null
if (-not $repoRoot) {
    throw "Not inside a git repository yet. Run 'git init' (or clone the repo) first, then re-run this script from within it."
}

git config filter.godotstripmcp.clean "python tools/strip-mcp-refs.py"
git config filter.godotstripmcp.smudge cat

Write-Host "Registered git filter 'godotstripmcp':" -ForegroundColor Green
Write-Host "  clean  = python tools/strip-mcp-refs.py" -ForegroundColor Green
Write-Host "  smudge = cat (passthrough -- we never re-inject MCP config on checkout)" -ForegroundColor Green
Write-Host "project.godot commits from this clone will now have the godot_mcp addon footprint stripped automatically." -ForegroundColor Green
