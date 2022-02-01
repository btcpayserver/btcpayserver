#!/bin/zsh

set -e

if [ -z "$1" ]; then
  echo "Please provide a version number"
  exit 1
fi

PLUGINS_DIR=../../plugins/
v=$1
t="LNbank v${v}"

sed -i "s%<AssemblyVersion>.*</AssemblyVersion>%<AssemblyVersion>${v}</AssemblyVersion>%g" ./BTCPayServer.Plugins.LNbank.csproj
sed -i "s%<PackageVersion>.*</PackageVersion>%<PackageVersion>${v}</PackageVersion>%g" ./BTCPayServer.Plugins.LNbank.csproj

./pack.sh

# Prepare plugins repo
cd $PLUGINS_DIR
git reset --hard
git checkout master
git pull --rebase
git checkout lnbank
git rebase master
cd -

# Copy and create PR for the plugin
cp bin/packed/* $PLUGINS_DIR
cd $PLUGINS_DIR
git add .
git commit -m $t
git push
gh pr create --title $t
cd -

# Commit and tag
git add .
git commit -m $t
git tag "BTCPayServer.LNbank/v$v"
git push && git push --tags
