using System;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Abstractions.Extensions;

public static class HttpRequestExtensions
{
    public static bool IsOnion(this HttpRequest request)
    {
        if (request?.Host.Host == null)
            return false;
        return request.Host.Host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetAbsoluteRoot(this HttpRequest request)
    {
        return string.Concat(
            request.Scheme,
            "://",
            request.Host.ToUriComponent(),
            request.PathBase.ToUriComponent());
    }

    public static Uri GetAbsoluteRootUri(this HttpRequest request)
    {
        return new Uri(request.GetAbsoluteRoot());
    }

    public static string GetCurrentUrl(this HttpRequest request)
    {
        return string.Concat(
            request.Scheme,
            "://",
            request.Host.ToUriComponent(),
            request.PathBase.ToUriComponent(),
            request.Path.ToUriComponent());
    }

    public static string GetCurrentUrlWithQueryString(this HttpRequest request)
    {
        return string.Concat(
            request.Scheme,
            "://",
            request.Host.ToUriComponent(),
            request.PathBase.ToUriComponent(),
            request.Path.ToUriComponent(),
            request.QueryString.ToUriComponent());
    }

    public static string GetCurrentPath(this HttpRequest request)
    {
        return string.Concat(
            request.PathBase.ToUriComponent(),
            request.Path.ToUriComponent());
    }

    public static string GetCurrentPathWithQueryString(this HttpRequest request)
    {
        return request.PathBase + request.Path + request.QueryString;
    }

    /// <summary>
    /// If 'toto' and RootPath is 'rootpath' returns '/rootpath/toto'
    /// If 'toto' and RootPath is empty returns '/toto'
    /// </summary>
    /// <param name="request"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    public static string GetRelativePath(this HttpRequest request, string path)
    {
        if (path.Length > 0 && path[0] != '/')
            path = $"/{path}";
        return string.Concat(
            request.PathBase.ToUriComponent(),
            path);
    }

    /// <summary>
    /// If 'https://example.com/toto' returns 'https://example.com/toto'
    /// If 'toto' and RootPath is 'rootpath' returns '/rootpath/toto'
    /// If 'toto' and RootPath is empty returns '/toto'
    /// </summary>
    /// <param name="request"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    public static string GetRelativePathOrAbsolute(this HttpRequest request, string path)
    {
        if (!Uri.TryCreate(path, UriKind.RelativeOrAbsolute, out var uri) ||
            uri.IsAbsoluteUri)
            return path;

        if (path.Length > 0 && path[0] != '/')
            path = $"/{path}";
        return string.Concat(
            request.PathBase.ToUriComponent(),
            path);
    }

    public static string GetAbsoluteUri(this HttpRequest request, string redirectUrl)
    {
        bool isRelative =
            (redirectUrl.Length > 0 && redirectUrl[0] == '/')
            || !new Uri(redirectUrl, UriKind.RelativeOrAbsolute).IsAbsoluteUri;
        return isRelative ? request.GetAbsoluteRoot() + redirectUrl : redirectUrl;
    }

    /// <summary>
    /// Will return an absolute URL. 
    /// If `relativeOrAbsolute` is absolute, returns it.
    /// If `relativeOrAbsolute` is relative, send absolute url based on the HOST of this request (without PathBase)
    /// </summary>
    /// <param name="request"></param>
    /// <param name="relativeOrAbsolte"></param>
    /// <returns></returns>
    public static Uri GetAbsoluteUriNoPathBase(this HttpRequest request, Uri relativeOrAbsolute = null)
    {
        if (relativeOrAbsolute == null)
        {
            return new Uri(string.Concat(
                request.Scheme,
                "://",
                request.Host.ToUriComponent()), UriKind.Absolute);
        }
        if (relativeOrAbsolute.IsAbsoluteUri)
            return relativeOrAbsolute;
        return new Uri(string.Concat(
            request.Scheme,
            "://",
            request.Host.ToUriComponent()) + relativeOrAbsolute.ToString().WithStartingSlash(), UriKind.Absolute);
    }
}
