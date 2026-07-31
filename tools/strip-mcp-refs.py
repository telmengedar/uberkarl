#!/usr/bin/env python3
"""
strip-mcp-refs.py -- git `clean` filter for project.godot.

Strips the godot_mcp dev-addon footprint (autoload singletons + the
editor_plugins `enabled` entry) out of project.godot before its content is
written to a git blob, so the MCP plugin's local activation never lands in
commit history. Registered via .gitattributes + tools/setup-git-filters.ps1.

Usage (this is a git clean filter, not meant to be run manually):
    git config filter.godotstripmcp.clean "python tools/strip-mcp-refs.py"
    git config filter.godotstripmcp.smudge cat

    Git then pipes the worktree content of project.godot through this
    script's stdin on `git add` / `git commit` / `git diff`, and uses
    stdout as the blob content. There is no smudge-side transform: we do
    not re-inject the MCP config on checkout (see .DESCRIPTION below).

Design notes:
  - Matches on the `res://addons/godot_mcp/` path, not on hardcoded
    singleton names (MCPScreenshot, MCPInputService, ...), so renaming or
    adding MCP autoload services doesn't require touching this script.
  - Only touches [autoload] lines and the editor_plugins `enabled=`
    array. Every other line -- other sections, comments, blank lines,
    ordering -- is passed through byte-for-byte, including its original
    line ending.
  - If stripping empties [autoload] or [editor_plugins] of all keys, the
    now-empty section header (and its body) is dropped too, so no dangling
    `[autoload]` with nothing under it is left behind.
  - Idempotent: running it on already-clean content is a no-op. Running it
    on content with no godot_mcp references at all (or missing sections)
    is also a no-op.
  - Deliberately has no smudge counterpart: re-enabling the MCP plugin
    locally (Project > Project Settings > Plugins) regenerates the
    autoload + enabled-plugin entries in the working tree on its own; we
    don't want git re-adding them on checkout.
"""
from __future__ import annotations

import re
import sys

MCP_PATH = "res://addons/godot_mcp/"
MCP_PLUGIN_CFG = "res://addons/godot_mcp/plugin.cfg"

SECTION_RE = re.compile(r"^\[(.+)\]\s*$")
QUOTED_ENTRY_RE = re.compile(r'"((?:[^"\\]|\\.)*)"')


def _line_body(line: str) -> str:
    """Return `line` with its trailing newline (\\n, \\r\\n or \\r) removed."""
    return line.rstrip("\r\n")


def _is_key_line(line: str) -> bool:
    """True if `line` looks like a `key=value` assignment (not blank/comment)."""
    body = _line_body(line).strip()
    if not body or body.startswith(";"):
        return False
    return "=" in body


def _strip_autoload_section(body_lines: list) -> list:
    """Drop any [autoload] line whose value references res://addons/godot_mcp/."""
    kept = []
    for line in body_lines:
        if _is_key_line(line):
            _, _, value = _line_body(line).partition("=")
            if MCP_PATH in value:
                continue  # drop this autoload entry
        kept.append(line)
    return kept


def _strip_editor_plugins_section(body_lines: list) -> list:
    """Remove the godot_mcp entry from editor_plugins/enabled=PackedStringArray(...)."""
    kept = []
    for line in body_lines:
        stripped = _line_body(line)
        m = re.match(r"^(\s*)enabled\s*=\s*PackedStringArray\((.*)\)\s*$", stripped)
        if not m:
            kept.append(line)
            continue

        indent, inner = m.group(1), m.group(2)
        entries = QUOTED_ENTRY_RE.findall(inner)
        remaining = [e for e in entries if e != MCP_PLUGIN_CFG]

        if not remaining:
            continue  # sole entry was godot_mcp -> drop the whole enabled= line

        if len(remaining) == len(entries):
            kept.append(line)  # nothing matched godot_mcp -> keep byte-identical
            continue

        line_ending = line[len(stripped):]
        rebuilt_inner = ", ".join(f'"{e}"' for e in remaining)
        kept.append(f"{indent}enabled=PackedStringArray({rebuilt_inner}){line_ending}")
    return kept


SECTION_STRIPPERS = {
    "autoload": _strip_autoload_section,
    "editor_plugins": _strip_editor_plugins_section,
}


def strip_mcp_refs(text: str) -> str:
    lines = text.splitlines(keepends=True)

    # Partition into a leading preamble (everything before the first
    # [section] header -- comments, config_version=5) and an ordered list
    # of (header_line, body_lines) blocks, one per section.
    preamble = []
    sections = []  # list[tuple[str, list[str]]]
    current_body = None

    for line in lines:
        m = SECTION_RE.match(_line_body(line))
        if m:
            sections.append((line, []))
            current_body = sections[-1][1]
        elif current_body is None:
            preamble.append(line)
        else:
            current_body.append(line)

    out = list(preamble)
    for header, body in sections:
        name = SECTION_RE.match(_line_body(header)).group(1)
        stripper = SECTION_STRIPPERS.get(name)
        new_body = stripper(body) if stripper is not None else body

        if stripper is not None and not any(_is_key_line(l) for l in new_body):
            # A stripper ran on this section and no keys survive -> drop
            # the header (and body) entirely instead of leaving an empty
            # `[autoload]` with nothing under it.
            continue

        out.append(header)
        out.extend(new_body)

    return "".join(out)


def main() -> None:
    raw = sys.stdin.buffer.read()
    text = raw.decode("utf-8")
    result = strip_mcp_refs(text)
    sys.stdout.buffer.write(result.encode("utf-8"))


if __name__ == "__main__":
    main()
