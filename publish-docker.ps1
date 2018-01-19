$ver = [regex]::Match((Get-Content BTCPayServer\BTCPayServer.csproj), '<Version>([^<]+)<').Groups[1].Value
git tag -a "v$ver" -m "$ver"
git push --tags