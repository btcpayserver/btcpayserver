#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.Extensions.Localization;

namespace BTCPayServer;

public class NullStringLocalizer : IStringLocalizer
{
    public static readonly NullStringLocalizer Instance = new();
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();

    public LocalizedString this[string name] => new(name, name);

    public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name));
}

public class NullViewLocalizer : IViewLocalizer
{
    public static readonly NullViewLocalizer Instance = new();
    public LocalizedString GetString(string name) => new(name, name);

    public LocalizedString GetString(string name, params object[] arguments) => new(name, string.Format(name));

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();

    public LocalizedHtmlString this[string name]  => new(name, name);

    public LocalizedHtmlString this[string name, params object[] arguments]  => new(name, string.Format(name));
}
