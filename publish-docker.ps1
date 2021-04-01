$ver = [regex]::Match((Get-Content Build/Version.csproj), '<Version>([^<]+)<').Groups[1].Value
git tag -a "v$ver" -m "$ver"
git checkout master
git push origin "v$ver" --force
