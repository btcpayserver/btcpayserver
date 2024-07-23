param(
    [string]$suffix
)

if ($suffix)
{
    $suffix = "-$suffix"
}


$ver = [regex]::Match((Get-Content Build/Version.csproj), '<Version>([^<]+)<').Groups[1].Value
git tag -a "v$ver$suffix" -m "$ver$suffix"
git checkout master
git push origin "v$ver$suffix" --force:bc1q4k4zlga72f0t0jrsyh93dzv2k7upry6an304jp