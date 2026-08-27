#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

gpg --batch --import "$SCRIPT_DIR"/*.asc
echo "Checking commit signature..."
status=$(git log -1 --format="%G?" HEAD)

case "$status" in
  G|U) ;;
  *)
    echo "ERROR: commit is not properly signed (status: $status)"
    exit 1
    ;;
esac

echo "The commit is signed"
git log -1 --show-signature
