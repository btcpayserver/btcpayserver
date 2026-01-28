#!/bin/sh
set -e

echo "Checking commit signature..."
status=$(git log -1 --format="%G?" HEAD)

case "$status" in
  G|U)  ;;   # signed (trusted or not)
  *) 
    echo "ERROR: commit is not properly signed (status: $status)"
    exit 1
    ;;
esac
