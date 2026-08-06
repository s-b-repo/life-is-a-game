#!/usr/bin/env bash
# Prune orphan .meta files whose companion file/folder doesn't exist.
#
# Unity generates a .meta sidecar for every asset. When the asset is deleted
# outside Unity (via git, mv, rm), its .meta is left behind and Unity spams
# warnings on next open. This script walks the tree, checks each .meta's
# expected companion, and deletes orphans.
#
# Usage: ./tools/prune-orphan-meta.sh [ROOT]
#   ROOT defaults to "Assets" if omitted.
#
# Dry-run by default. Pass --apply to actually delete.

set -euo pipefail

ROOT="${1:-Assets}"
APPLY=0
for arg in "$@"; do
  [ "$arg" = "--apply" ] && APPLY=1
done

removed=0
kept=0

# find -print0 handles filenames with spaces
while IFS= read -r -d '' metafile; do
  # Strip trailing ".meta" — the companion has the same name minus ".meta"
  companion="${metafile%.meta}"
  if [ -e "$companion" ]; then
    kept=$((kept+1))
  else
    if [ $APPLY -eq 1 ]; then
      git rm -q -- "$metafile" 2>/dev/null || rm -f -- "$metafile"
      echo "REMOVED: $metafile"
    else
      echo "ORPHAN:  $metafile"
    fi
    removed=$((removed+1))
  fi
done < <(find "$ROOT" -name '*.meta' -print0)

echo
echo "Kept:    $kept"
echo "Orphans: $removed"
[ $APPLY -eq 0 ] && echo "(dry-run — pass --apply to delete)"
