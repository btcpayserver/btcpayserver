using System;
using System.Text.RegularExpressions;

namespace BTCPayServer.Abstractions.Routing;

public sealed class RegexRouteConvention
{
    public RegexRouteConvention(string routeParameterName, string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(routeParameterName);
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        RouteParameterName = routeParameterName;
        Pattern = pattern;
        Regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    public string RouteParameterName { get; }
    public string Pattern { get; }
    public Regex Regex { get; }
}
