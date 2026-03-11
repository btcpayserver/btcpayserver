#!/usr/bin/env bash
set -euo pipefail

suffix="${1-}"

if [[ -n "$suffix" ]]; then
  suffix="-$suffix"
fi

ver="$(sed -n 's/.*<Version>\([^<]*\)<.*/\1/p' Build/Version.csproj | head -n 1)"

git tag -a "v${ver}${suffix}" -m "${ver}${suffix}"
git checkout master
git push origin "v${ver}${suffix}" --force
