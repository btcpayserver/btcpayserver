using System;
using System.IO;
using System.Linq;

namespace BTCPayServer.Abstractions.Extensions;

public static class StringExtensions
{
    public static bool IsValidFileName(this string fileName)
    {
        return !fileName.ToCharArray().Any(c => Path.GetInvalidFileNameChars().Contains(c)
                                                || c == Path.AltDirectorySeparatorChar
                                                || c == Path.DirectorySeparatorChar
                                                || c == Path.PathSeparator
                                                || c == '\\');
    }

    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    public static string WithTrailingSlash(this string str)
    {
        if (str.EndsWith("/", StringComparison.InvariantCulture))
            return str;
        return str + "/";
    }
    public static string WithStartingSlash(this string str)
    {
        if (str.StartsWith("/", StringComparison.InvariantCulture))
            return str;
        return $"/{str}";
    }
    public static string WithoutEndingSlash(this string str)
    {
        if (str.EndsWith("/", StringComparison.InvariantCulture))
            return str.Substring(0, str.Length - 1);
        return str;
    }
}
