#nullable enable
using System;
using System.Linq;

namespace BTCPayServer.Services.Apps;

public static class CrowdfundResourcePolicy
{
    public const int MaxSounds = 50;
    public const int MaxSoundLength = 2_048;
    public const int MaxTotalSoundCharacters = 20_000;
    public static bool IsSafePassiveResourceUrl(string url, Uri? baseUri)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        if (url.StartsWith("fileid:", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme is not ("http" or "https"))
            return false;
        if (baseUri is null)
            return true;
        return !string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase);
    }

    public static bool AreValidSounds(string[] sounds, Uri? baseUri)
    {
        return sounds is { Length: > 0 and <= MaxSounds } &&
               sounds.All(sound =>
                   !string.IsNullOrWhiteSpace(sound) &&
                   sound.Length <= MaxSoundLength &&
                   IsSafePassiveResourceUrl(sound, baseUri)) &&
               sounds.Sum(sound => (long)sound.Length) <= MaxTotalSoundCharacters;
    }
}
