using System;
using System.Collections.Generic;
using System.Linq;
using Ganss.Xss;

namespace BTCPayServer.Hosting;

internal static class HtmlSanitizerFactory
{
    private static readonly string[] FormTags =
    [
        "button",
        "datalist",
        "fieldset",
        "form",
        "input",
        "keygen",
        "label",
        "legend",
        "optgroup",
        "option",
        "output",
        "select",
        "textarea"
    ];

    private static readonly HashSet<string> AllowedIframePermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "accelerometer",
        "autoplay",
        "encrypted-media",
        "gyroscope",
        "picture-in-picture",
        "web-share"
    };

    private static readonly HashSet<string> IframeHostsAllowedSameOrigin = new(StringComparer.OrdinalIgnoreCase)
    {
        "www.youtube.com",
        "www.youtube-nocookie.com",
        "www.facebook.com",
        "vine.co",
        "instagram.com",
        "player.vimeo.com",
        "www.dailymotion.com",
        "player.youku.com",
        "jumpingbean.tv",
        "v.qq.com",
        "rumble.com",
        "twitter.com",
        "syndication.twitter.com",
        "www.tiktok.com",
        "www.linkedin.com",
        "embed.reddit.com",
        "www.redditmedia.com",
        "player.twitch.tv",
        "clips.twitch.tv",
        "open.spotify.com",
        "w.soundcloud.com",
        "embed.bsky.app",
        "www.threads.net",
        "www.threads.com",
        "www.pinterest.com",
        "assets.pinterest.com",
        "discord.com",
        "www.snapchat.com"
    };

    internal static HtmlSanitizer Create()
    {
        var htmlSanitizer = new HtmlSanitizer();
        htmlSanitizer.AllowDataAttributes = false;
        RemoveFormTags(htmlSanitizer);
        htmlSanitizer.AllowedTags.Remove("img");
        htmlSanitizer.AllowedTags.Remove("iframe");
        htmlSanitizer.AllowedTags.Remove("style");
        htmlSanitizer.AllowedTags.Remove("video");
        htmlSanitizer.AllowedAttributes.Remove("style");
        htmlSanitizer.AllowedAttributes.Add("class");
        htmlSanitizer.AllowedSchemes.Add("mailto");
        htmlSanitizer.AllowedSchemes.Add("bitcoin");
        htmlSanitizer.AllowedSchemes.Add("lightning");
        return htmlSanitizer;
    }

    internal static HtmlSanitizer CreateRichText()
    {
        var htmlSanitizer = new HtmlSanitizer();
        htmlSanitizer.AllowDataAttributes = false;
        RemoveFormTags(htmlSanitizer);

        htmlSanitizer.AllowedCssProperties.Clear();
        foreach (var property in new[]
                 {
                     "background-color",
                     "border-color",
                     "border-radius",
                     "border-style",
                     "border-width",
                     "color",
                     "font-family",
                     "font-size",
                     "font-style",
                     "font-weight",
                     "letter-spacing",
                     "line-height",
                     "list-style-position",
                     "list-style-type",
                     "overflow-wrap",
                     "text-align",
                     "text-decoration",
                     "text-transform",
                     "vertical-align",
                     "white-space",
                     "word-break",
                     "word-spacing"
                 })
        {
            htmlSanitizer.AllowedCssProperties.Add(property);
        }
        htmlSanitizer.PostProcessNode += (sender, args) =>
        {
            if (args.Node is not AngleSharp.Dom.IElement element)
                return;

            if (element.LocalName.Equals("img", StringComparison.OrdinalIgnoreCase))
            {
                element.ClassList.Add("img-fluid");
            }
            else if (element.LocalName.Equals("iframe", StringComparison.OrdinalIgnoreCase))
            {
                var source = element.GetAttribute("src");
                var isTrustedSource = IsTrustedIframeSource(source);
                var sandbox = "allow-scripts allow-forms allow-popups";
                if (isTrustedSource)
                {
                    sandbox += " allow-same-origin";
                }
                element.SetAttribute("sandbox", sandbox);
                element.SetAttribute("referrerpolicy", "strict-origin-when-cross-origin");

                if (element.GetAttribute("allow") is { } allow &&
                    allow.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Any(permission => !IsAllowedIframePermission(permission, isTrustedSource)))
                {
                    element.RemoveAttribute("allow");
                }

                if (element.GetAttribute("loading") is { } and not ("eager" or "lazy"))
                {
                    element.RemoveAttribute("loading");
                }

                if (element.GetAttribute("frameborder") is { } frameBorder && frameBorder != "0")
                {
                    element.RemoveAttribute("frameborder");
                }
            }
        };

        htmlSanitizer.AllowedAttributes.Add("class");
        htmlSanitizer.AllowedTags.Add("iframe");
        htmlSanitizer.AllowedTags.Add("video");
        htmlSanitizer.AllowedAttributes.Add("allow");
        htmlSanitizer.AllowedAttributes.Add("controls");
        htmlSanitizer.AllowedAttributes.Add("frameborder");
        htmlSanitizer.AllowedAttributes.Add("loading");
        htmlSanitizer.AllowedAttributes.Add("webkitallowfullscreen");
        htmlSanitizer.AllowedAttributes.Add("allowfullscreen");
        htmlSanitizer.AllowedSchemes.Add("mailto");
        htmlSanitizer.AllowedSchemes.Add("bitcoin");
        htmlSanitizer.AllowedSchemes.Add("lightning");
        return htmlSanitizer;
    }

    private static void RemoveFormTags(HtmlSanitizer sanitizer)
    {
        foreach (var tag in FormTags)
        {
            sanitizer.AllowedTags.Remove(tag);
        }
    }

    private static bool IsTrustedIframeSource(string source) =>
        TryCreateIframeUri(source, out var uri) && IframeHostsAllowedSameOrigin.Contains(uri.IdnHost);

    private static bool IsAllowedIframePermission(string permission, bool isTrustedSource) =>
        AllowedIframePermissions.Contains(permission) ||
        (isTrustedSource && permission.Equals("clipboard-write", StringComparison.OrdinalIgnoreCase));

    private static bool TryCreateIframeUri(string source, out Uri uri)
    {
        if (source?.StartsWith("//", StringComparison.Ordinal) is true)
        {
            source = $"https:{source}";
        }

        return Uri.TryCreate(source, UriKind.Absolute, out uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
               string.IsNullOrEmpty(uri.UserInfo);
    }
}
