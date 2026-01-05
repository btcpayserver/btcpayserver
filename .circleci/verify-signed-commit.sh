#!/bin/sh
set -e

echo "Checking commit signature..."
git log -1 --format="%G?" HEAD | grep -qE "G|U|E|X|Y|R"
