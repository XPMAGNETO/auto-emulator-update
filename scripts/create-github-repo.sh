#!/usr/bin/env bash
set -euo pipefail

REPO="${1:-auto-emulator-update}"
VISIBILITY="${2:-public}"

command -v gh >/dev/null || { echo "GitHub CLI (gh) is required."; exit 1; }
gh auth status
OWNER="$(gh api user --jq .login)"
test -n "$OWNER"

python3 - "$OWNER" "$REPO" <<'PY'
from pathlib import Path
import sys
owner, repo = sys.argv[1:3]
p = Path("src/AutoEmulatorUpdate.Core/BuildInfo.cs")
s = p.read_text()
s = s.replace("OWNER/auto-emulator-update", f"{owner}/{repo}")
p.write_text(s)
PY

git add src/AutoEmulatorUpdate.Core/BuildInfo.cs
git commit -m "chore: configure GitHub update repository" || true

gh repo create "$REPO" "--$VISIBILITY" --source=. --remote=origin --push
git push origin --tags

echo "Repository created: https://github.com/$OWNER/$REPO"
