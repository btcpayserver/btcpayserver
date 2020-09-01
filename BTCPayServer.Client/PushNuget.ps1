rm "bin\release\" -Recurse -Force
dotnet pack --configuration Release --include-symbols -p:SymbolPackageFormat=snupkg
$package=(ls .\bin\Release\*.nupkg).FullName
dotnet nuget push $package --source "https://api.nuget.org/v3/index.json"
$ver = ((ls .\bin\release\*.nupkg)[0].Name -replace '.*(\d+\.\d+\.\d+)\.nupkg','$1')
git tag -a "BTCPayServer.Client/v$ver" -m "BTCPayServer.Client/$ver"
git push origin "BTCPayServer.Client/v$ver"
