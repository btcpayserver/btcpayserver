$ver = [regex]::Match((Get-Content BTCPayServer\BTCPayServer.csproj), '<Version>([^<]+)<').Groups[1].Value
git tag -a "v$ver" -m "$ver"
git checkout latest
git merge master
git checkout master
git tag -d "stable"
git tag -a "stable" -m "stable"
git push origin latest master --tags --force
