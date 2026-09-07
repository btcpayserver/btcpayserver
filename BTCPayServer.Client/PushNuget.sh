#!/bin/bash
set -euo pipefail

rm -rf "bin/Release/"
dotnet pack --configuration Release --include-symbols -p:SymbolPackageFormat=snupkg

package=$(find ./bin/Release -name "BTCPayServer.Client.*.nupkg" -type f -print -quit)
if [[ -z "$package" ]]; then
    echo "BTCPayServer.Client package not found" >&2
    exit 1
fi

dotnet nuget push "$package" --source "https://api.nuget.org/v3/index.json" --api-key "$NUGET_API_KEY"
ver=$(basename "$package" | sed -E 's/BTCPayServer\.Client\.([0-9]+(\.[0-9]+){1,3})\.nupkg/\1/')
git tag -a "BTCPayServer.Client/v$ver" -m "BTCPayServer.Client/$ver"
git push origin "BTCPayServer.Client/v$ver"
