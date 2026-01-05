#!/bin/sh
set -e

echo "Verifying commit signature..."
git verify-commit HEAD
