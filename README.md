# Uberkarl

A Godot 4.7.1 game project.

## Dev setup

### 1. One-time git filter setup

This repo strips the `godot_mcp` editor addon's footprint out of
`project.godot` automatically before it's committed (see below), using a
git `clean` filter. Git does **not** register `filter.*` config on
`clone`/`init` for security reasons, so run this once, right after you
clone (or `git init`) the repo:

```powershell
.\tools\setup-git-filters.ps1
```

That's equivalent to running, from the repo root:

```sh
git config filter.godotstripmcp.clean "python tools/strip-mcp-refs.py"
git config filter.godotstripmcp.smudge cat
```

Requires Python 3 on `PATH`.

### 2. The godot_mcp addon (dev tool, not game content)

`addons/godot_mcp/` is [godot-mcp-pro](https://github.com/youichi-uda/godot-mcp-pro)'s
free editor plugin -- it lets an AI coding agent drive the Godot editor
over a WebSocket. It's a developer convenience, not part of the shipped
game, so it's gitignored
(`/addons/godot_mcp/` in `.gitignore`) and never committed.

Enabling the plugin locally writes its autoload singletons
(`MCPScreenshot`, `MCPInputService`, `MCPGameInspector`) and its entry in
`editor_plugins/enabled` into your local `project.godot`. That's fine --
the `godotstripmcp` clean filter (set up above) strips those references
out automatically whenever `project.godot` is committed, so they never
land in git history and never affect a teammate's or CI's checkout. Your
own working copy of `project.godot` keeps them; only the committed blob is
cleaned.

**On a fresh clone**, if you want the MCP addon:

1. Re-obtain `addons/godot_mcp/` (it isn't in the repo -- get it from
   wherever the team distributes it).
2. In the Godot editor: **Project ▸ Project Settings ▸ Plugins**, enable
   `godot_mcp`.

If you don't use the MCP addon, there's nothing to do -- the project runs
fine without it.

## Project layout notes

- `config_version=5`, Godot **4.7.1**.
- `*.import` files **are** committed (Godot needs them for reproducible
  imports) -- don't add them to `.gitignore`.
- `export_presets.cfg` is gitignored (can hold keystore/signing secrets);
  set up your own locally via **Project ▸ Export**.
