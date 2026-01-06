#!/bin/sh
set -e

echo "Checking commit signature..."
if git log -1 --format="%G?" HEAD | grep -q "^N$"; then
    echo "ERROR: Commit is not signed"
    exit 1
fi
