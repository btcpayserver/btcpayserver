using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Routing;
using BTCPayServer.Client.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Hosting;

internal static class RegexRouteConventionRouting
{
    internal const string ConstraintName = "btcpayRegex";

    public static void Register(IServiceCollection services)
    {
        services.AddRouting(options => options.ConstraintMap[ConstraintName] = typeof(RegexRouteConstraint));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<MvcOptions>, RegexRouteConventionMvcOptionsSetup>());
    }
}

internal sealed class RegexRouteConventionMvcOptionsSetup(
    IEnumerable<RegexRouteConvention> conventions) : IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions options)
    {
        options.Conventions.Add(new RegexRouteApplicationModelConvention(conventions));
    }
}

internal sealed class RegexRouteApplicationModelConvention(IEnumerable<RegexRouteConvention> conventions)
    : IApplicationModelConvention
{
    private readonly IReadOnlyDictionary<string, RegexRouteConvention> _conventions = conventions
        .ToDictionary(c => c.RouteParameterName, StringComparer.Ordinal);

    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            Apply(controller.Selectors);
            foreach (var action in controller.Actions)
                Apply(action.Selectors);
        }
    }

    private void Apply(IEnumerable<SelectorModel> selectors)
    {
        foreach (var selector in selectors)
        {
            var templateText = selector.AttributeRouteModel?.Template;
            if (string.IsNullOrEmpty(templateText))
                continue;

            var template = TemplateParser.Parse(templateText);
            var updatedTemplate = templateText;
            foreach (var parameter in template.Parameters)
            {
                if (!_conventions.ContainsKey(parameter.Name) ||
                    parameter.InlineConstraints.Any(c =>
                        string.Equals(c.Constraint, RegexRouteConventionRouting.ConstraintName,
                            StringComparison.Ordinal)))
                {
                    continue;
                }

                updatedTemplate = System.Text.RegularExpressions.Regex.Replace(
                    updatedTemplate,
                    $@"(\{{\*{{0,2}}{System.Text.RegularExpressions.Regex.Escape(parameter.Name)})(?=[:?=}}])",
                    $"$1:{RegexRouteConventionRouting.ConstraintName}");
            }

            selector.AttributeRouteModel.Template = updatedTemplate;
        }
    }
}

internal sealed class RegexRouteConstraint(IEnumerable<RegexRouteConvention> conventions) : IRouteConstraint
{
    private readonly IReadOnlyDictionary<string, RegexRouteConvention> _conventions = conventions
        .ToDictionary(c => c.RouteParameterName, StringComparer.Ordinal);

    public bool Match(HttpContext httpContext, IRouter route, string routeKey,
        RouteValueDictionary values, RouteDirection routeDirection)
    {
        if (!_conventions.TryGetValue(routeKey, out var convention) ||
            !values.TryGetValue(routeKey, out var value) || value is null)
        {
            return true;
        }

        var isMatch = convention.Regex.IsMatch(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        return routeDirection == RouteDirection.IncomingRequest || isMatch;
    }
}

internal sealed class RegexRouteConventionMiddleware(
    RequestDelegate next,
    IEnumerable<RegexRouteConvention> conventions)
{
    private readonly IReadOnlyDictionary<string, RegexRouteConvention> _conventions = conventions
        .ToDictionary(c => c.RouteParameterName, StringComparer.Ordinal);

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint() is not RouteEndpoint endpoint)
        {
            await next(context);
            return;
        }

        var errors = new List<GreenfieldValidationError>();
        foreach (var parameter in endpoint.RoutePattern.Parameters)
        {
            if (!parameter.ParameterPolicies.Any(p =>
                    string.Equals(p.Content, RegexRouteConventionRouting.ConstraintName, StringComparison.Ordinal)) ||
                !_conventions.TryGetValue(parameter.Name, out var convention) ||
                !context.Request.RouteValues.TryGetValue(parameter.Name, out var value) || value is null)
            {
                continue;
            }

            if (!convention.Regex.IsMatch(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty))
            {
                errors.Add(new GreenfieldValidationError(parameter.Name,
                    $"The value does not match the expected format '{convention.Pattern}'."));
            }
        }

        if (errors.Count is 0)
        {
            await next(context);
            return;
        }

        var route = endpoint.RoutePattern.RawText?.TrimStart('~', '/') ?? string.Empty;
        if (route.StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            await context.Response.WriteAsJsonAsync(errors);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }
}
