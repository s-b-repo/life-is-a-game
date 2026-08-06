#!/usr/bin/env python3
"""Strip MonoBehaviour components whose script GUID doesn't resolve.

Unity scene/prefab YAML stores MonoBehaviour instances with `m_Script` refs
of the form:

    m_Script: {fileID: 11500000, guid: <32-hex>, type: 3}

If no `.cs.meta` in the project declares that guid, the Editor renders the
component as a yellow "Referenced script is missing" placeholder that spams
the Console on every scene load. This script:

  1. Walks all `.cs.meta` files under Assets/ and collects declared GUIDs.
  2. For each scene/prefab passed on the command line, finds MonoBehaviour
     blocks whose m_Script guid isn't in that set.
  3. Removes those MonoBehaviour blocks and the corresponding entry in the
     parent GameObject's `m_Component` list.

Usage:
    ./tools/strip-missing-scripts.py [--apply] SCENE.unity [SCENE2.unity ...]

Dry-run by default; pass --apply to overwrite the scene file(s).
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path


META_GUID_RE = re.compile(r"^guid:\s*([0-9a-f]{32})", re.MULTILINE)
BLOCK_START_RE = re.compile(r"^--- !u!(\d+) &(\d+)(?:\s+stripped)?$", re.MULTILINE)
SCRIPT_GUID_RE = re.compile(
    r"m_Script:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-f]{32}),\s*type:\s*3\}"
)
COMPONENT_REF_RE = re.compile(r"^\s*-\s*component:\s*\{fileID:\s*(\d+)\}\s*$", re.MULTILINE)


def collect_declared_guids(assets_root: Path) -> set[str]:
    guids: set[str] = set()
    for meta in assets_root.rglob("*.cs.meta"):
        try:
            text = meta.read_text(encoding="utf-8", errors="ignore")
        except OSError:
            continue
        m = META_GUID_RE.search(text)
        if m:
            guids.add(m.group(1))
    return guids


def find_blocks(text: str) -> list[tuple[int, int, int, int]]:
    """Return list of (start, end, class_id, file_id) for each `--- !u!...` block."""
    starts = list(BLOCK_START_RE.finditer(text))
    blocks: list[tuple[int, int, int, int]] = []
    for i, m in enumerate(starts):
        start = m.start()
        end = starts[i + 1].start() if i + 1 < len(starts) else len(text)
        blocks.append((start, end, int(m.group(1)), int(m.group(2))))
    return blocks


def strip_scene(path: Path, valid_guids: set[str]) -> tuple[str, int]:
    """Return (new_text, num_stripped)."""
    text = path.read_text(encoding="utf-8", errors="ignore")
    blocks = find_blocks(text)

    # Find MonoBehaviour blocks (class id 114) whose m_Script guid is unknown.
    dead_file_ids: set[int] = set()
    dead_block_ranges: list[tuple[int, int]] = []
    for start, end, class_id, file_id in blocks:
        if class_id != 114:
            continue
        chunk = text[start:end]
        m = SCRIPT_GUID_RE.search(chunk)
        if m is None:
            continue  # malformed; leave alone
        if m.group(1) in valid_guids:
            continue
        dead_file_ids.add(file_id)
        dead_block_ranges.append((start, end))

    if not dead_file_ids:
        return text, 0

    # Remove component references pointing at dead file_ids from every GameObject.
    def _drop_component_ref(match: re.Match[str]) -> str:
        return "" if int(match.group(1)) in dead_file_ids else match.group(0) + "\n"

    # First split off the dead blocks so we don't rewrite inside them.
    # Rebuild the text by keeping everything NOT in a dead block range,
    # then stripping component refs that pointed at those blocks.
    dead_block_ranges.sort()
    kept_parts: list[str] = []
    cursor = 0
    for s, e in dead_block_ranges:
        kept_parts.append(text[cursor:s])
        cursor = e
    kept_parts.append(text[cursor:])
    surviving = "".join(kept_parts)

    def _keep_or_drop(match: re.Match[str]) -> str:
        return "" if int(match.group(1)) in dead_file_ids else match.group(0) + "\n"

    # COMPONENT_REF_RE matches a full line; we must consume the trailing \n too.
    surviving = re.sub(
        r"(^\s*-\s*component:\s*\{fileID:\s*(\d+)\}\s*\n)",
        lambda m: "" if int(m.group(2)) in dead_file_ids else m.group(1),
        surviving,
        flags=re.MULTILINE,
    )

    return surviving, len(dead_block_ranges)


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("scenes", nargs="+", type=Path, help="Scene / prefab YAML files")
    p.add_argument("--apply", action="store_true", help="Overwrite files (default: dry-run)")
    p.add_argument("--assets", type=Path, default=Path("Assets"), help="Project Assets root")
    args = p.parse_args()

    valid = collect_declared_guids(args.assets)
    print(f"Collected {len(valid)} declared script GUIDs under {args.assets}/")

    total_stripped = 0
    for scene in args.scenes:
        new_text, n = strip_scene(scene, valid)
        total_stripped += n
        if n == 0:
            print(f"  {scene}: 0 dead scripts")
            continue
        print(f"  {scene}: {n} dead MonoBehaviour blocks{' (removing)' if args.apply else ' (dry-run)'}")
        if args.apply:
            scene.write_text(new_text, encoding="utf-8")

    print(f"\nTotal dead blocks: {total_stripped}")
    print("(dry-run — pass --apply to overwrite)" if not args.apply else "Done.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
