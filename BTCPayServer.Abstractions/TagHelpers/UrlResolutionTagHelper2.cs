#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor.TagHelpers;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;


namespace BTCPayServer.Abstractions.TagHelpers
{
    // A copy of https://github.com/dotnet/aspnetcore/blob/39f0e0b8f40b4754418f81aef0de58a9204a1fe5/src/Mvc/Mvc.Razor/src/TagHelpers/UrlResolutionTagHelper.cs
    // slightly modified to also work on use tag.
    public class UrlResolutionTagHelper2 : TagHelper
    {
        // Valid whitespace characters defined by the HTML5 spec.
        private static readonly char[] ValidAttributeWhitespaceChars =
            new[] { '\t', '\n', '\u000C', '\r', ' ' };
        private static readonly Dictionary<string, string[]> ElementAttributeLookups =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "use", new[] { "href" } },
                { "a", new[] { "href" } },
                { "applet", new[] { "archive" } },
                { "area", new[] { "href" } },
                { "audio", new[] { "src" } },
                { "base", new[] { "href" } },
                { "blockquote", new[] { "cite" } },
                { "button", new[] { "formaction" } },
                { "del", new[] { "cite" } },
                { "embed", new[] { "src" } },
                { "form", new[] { "action" } },
                { "html", new[] { "manifest" } },
                { "iframe", new[] { "src" } },
                { "img", new[] { "src", "srcset" } },
                { "input", new[] { "src", "formaction" } },
                { "ins", new[] { "cite" } },
                { "link", new[] { "href" } },
                { "menuitem", new[] { "icon" } },
                { "object", new[] { "archive", "data" } },
                { "q", new[] { "cite" } },
                { "script", new[] { "src" } },
                { "source", new[] { "src", "srcset" } },
                { "track", new[] { "src" } },
                { "video", new[] { "poster", "src" } },
            };

        /// <summary>
        /// Creates a new <see cref="UrlResolutionTagHelper"/>.
        /// </summary>
        /// <param name="urlHelperFactory">The <see cref="IUrlHelperFactory"/>.</param>
        /// <param name="htmlEncoder">The <see cref="HtmlEncoder"/>.</param>
        public UrlResolutionTagHelper2(IUrlHelperFactory urlHelperFactory, HtmlEncoder htmlEncoder)
        {
            UrlHelperFactory = urlHelperFactory;
            HtmlEncoder = htmlEncoder;
        }

        /// <inheritdoc />
        public override int Order => -1000 - 999;

        /// <summary>
        /// The <see cref="IUrlHelperFactory"/>.
        /// </summary>
        protected IUrlHelperFactory UrlHelperFactory { get; }

        /// <summary>
        /// The <see cref="HtmlEncoder"/>.
        /// </summary>
        protected HtmlEncoder HtmlEncoder { get; }

        /// <summary>
        /// The <see cref="ViewContext"/>.
        /// </summary>
        [HtmlAttributeNotBound]
        [ViewContext]
        public ViewContext ViewContext { get; set; } = default!;

        /// <inheritdoc />
        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(output);

            if (output.TagName == null)
            {
                return;
            }

            if (ElementAttributeLookups.TryGetValue(output.TagName, out var attributeNames))
            {
                for (var i = 0; i < attributeNames.Length; i++)
                {
                    ProcessUrlAttribute(attributeNames[i], output);
                }
            }

            // itemid can be present on any HTML element.
            ProcessUrlAttribute("itemid", output);
        }

        /// <summary>
        /// Resolves and updates URL values starting with '~/' (relative to the application's 'webroot' setting) for
        /// <paramref name="output"/>'s <see cref="TagHelperOutput.Attributes"/> whose
        /// <see cref="TagHelperAttribute.Name"/> is <paramref name="attributeName"/>.
        /// </summary>
        /// <param name="attributeName">The attribute name used to lookup values to resolve.</param>
        /// <param name="output">The <see cref="TagHelperOutput"/>.</param>
        protected void ProcessUrlAttribute(string attributeName, TagHelperOutput output)
        {
            ArgumentNullException.ThrowIfNull(attributeName);
            ArgumentNullException.ThrowIfNull(output);

            var attributes = output.Attributes;
            // Read interface .Count once rather than per iteration
            var attributesCount = attributes.Count;
            for (var i = 0; i < attributesCount; i++)
            {
                var attribute = attributes[i];
                if (!string.Equals(attribute.Name, attributeName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (attribute.Value is string stringValue)
                {
                    if (TryResolveUrl(stringValue, resolvedUrl: out string? resolvedUrl))
                    {
                        attributes[i] = new TagHelperAttribute(
                            attribute.Name,
                            resolvedUrl,
                            attribute.ValueStyle);
                    }
                }
                else
                {
                    if (attribute.Value is IHtmlContent htmlContent)
                    {
                        var htmlString = htmlContent as HtmlString;
                        if (htmlString != null)
                        {
                            // No need for a StringWriter in this case.
                            stringValue = htmlString.ToString();
                        }
                        else
                        {
                            using var writer = new StringWriter();
                            htmlContent.WriteTo(writer, HtmlEncoder);
                            stringValue = writer.ToString();
                        }

                        if (TryResolveUrl(stringValue, resolvedUrl: out IHtmlContent? resolvedUrl))
                        {
                            attributes[i] = new TagHelperAttribute(
                                attribute.Name,
                                resolvedUrl,
                                attribute.ValueStyle);
                        }
                        else if (htmlString == null)
                        {
                            // Not a ~/ URL. Just avoid re-encoding the attribute value later.
                            attributes[i] = new TagHelperAttribute(
                                attribute.Name,
                                new HtmlString(stringValue),
                                attribute.ValueStyle);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Tries to resolve the given <paramref name="url"/> value relative to the application's 'webroot' setting.
        /// </summary>
        /// <param name="url">The URL to resolve.</param>
        /// <param name="resolvedUrl">Absolute URL beginning with the application's virtual root. <c>null</c> if
        /// <paramref name="url"/> could not be resolved.</param>
        /// <returns><c>true</c> if the <paramref name="url"/> could be resolved; <c>false</c> otherwise.</returns>
        protected bool TryResolveUrl(string url, out string? resolvedUrl)
        {
            resolvedUrl = null;
            var start = FindRelativeStart(url);
            if (start == -1)
            {
                return false;
            }

            var trimmedUrl = CreateTrimmedString(url, start);

            var urlHelper = UrlHelperFactory.GetUrlHelper(ViewContext);
            resolvedUrl = urlHelper.Content(trimmedUrl);

            return true;
        }

        /// <summary>
        /// Tries to resolve the given <paramref name="url"/> value relative to the application's 'webroot' setting.
        /// </summary>
        /// <param name="url">The URL to resolve.</param>
        /// <param name="resolvedUrl">
        /// Absolute URL beginning with the application's virtual root. <c>null</c> if <paramref name="url"/> could
        /// not be resolved.
        /// </param>
        /// <returns><c>true</c> if the <paramref name="url"/> could be resolved; <c>false</c> otherwise.</returns>
        protected bool TryResolveUrl(string url, [NotNullWhen(true)] out IHtmlContent? resolvedUrl)
        {
            resolvedUrl = null;
            var start = FindRelativeStart(url);
            if (start == -1)
            {
                return false;
            }

            var trimmedUrl = CreateTrimmedString(url, start);

            var urlHelper = UrlHelperFactory.GetUrlHelper(ViewContext);
            var appRelativeUrl = urlHelper.Content(trimmedUrl);
            var postTildeSlashUrlValue = trimmedUrl.Substring(2);

            if (!appRelativeUrl.EndsWith(postTildeSlashUrlValue, StringComparison.Ordinal))
            {
                throw new InvalidOperationException();
            }

            resolvedUrl = new EncodeFirstSegmentContent(
                appRelativeUrl,
                appRelativeUrl.Length - postTildeSlashUrlValue.Length,
                postTildeSlashUrlValue);

            return true;
        }

        private static int FindRelativeStart(string url)
        {
            if (url == null || url.Length < 2)
            {
                return -1;
            }

            var maxTestLength = url.Length - 2;

            var start = 0;
            for (; start < url.Length; start++)
            {
                if (start > maxTestLength)
                {
                    return -1;
                }

                if (!IsCharWhitespace(url[start]))
                {
                    break;
                }
            }

            // Before doing more work, ensure that the URL we're looking at is app-relative.
            if (url[start] != '~' || url[start + 1] != '/')
            {
                return -1;
            }

            return start;
        }

        private static string CreateTrimmedString(string input, int start)
        {
            var end = input.Length - 1;
            for (; end >= start; end--)
            {
                if (!IsCharWhitespace(input[end]))
                {
                    break;
                }
            }

            var len = end - start + 1;

            // Substring returns same string if start == 0 && len == Length
            return input.Substring(start, len);
        }

        private static bool IsCharWhitespace(char ch)
        {
            return ValidAttributeWhitespaceChars.AsSpan().IndexOf(ch) != -1;
        }

        private sealed class EncodeFirstSegmentContent : IHtmlContent
        {
            private readonly string _firstSegment;
            private readonly int _firstSegmentLength;
            private readonly string _secondSegment;

            public EncodeFirstSegmentContent(string firstSegment, int firstSegmentLength, string secondSegment)
            {
                _firstSegment = firstSegment;
                _firstSegmentLength = firstSegmentLength;
                _secondSegment = secondSegment;
            }

            public void WriteTo(TextWriter writer, HtmlEncoder encoder)
            {
                encoder.Encode(writer, _firstSegment, 0, _firstSegmentLength);
                writer.Write(_secondSegment);
            }
        }
    }

}
