#!/bin/sh
#
# Enable the hooks in this directory.
#
# Husky's own installer is a Node package; this repository has no Node
# dependency, so the hooks are wired up the way Git supports natively: point
# core.hooksPath at this folder. Run once per clone.
#
#   sh .husky/install.sh

set -e

repo_root=$(cd "$(dirname "$0")/.." && pwd)

cd "$repo_root"
git config core.hooksPath .husky
chmod +x .husky/pre-commit .husky/commit-msg

echo "[husky] core.hooksPath set to .husky"
echo "[husky] active hooks: $(git config --get core.hooksPath)"
