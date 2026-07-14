#nullable enable
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.Breadcrumb;

public class TitleHeader : ViewComponent
{
    public IViewComponentResult Invoke(string? title = null, string? documentationUrl = null)
    {
        title ??= ViewData["Title"] as string;
        var resolvedItems = ViewData.GetBreadcrumbs()
            .Select(item => item.Url is null && item.Action is not null
                ? item with { Url = Url.Action(item.Action, GetControllerName(item.Controller), item.RouteValues) }
                : item)
            .ToList();

        if (!resolvedItems.Any(item => item.Active))
            resolvedItems.Add(new BreadcrumbItem(title, Active: true));

        return View(new BreadcrumbViewModel(resolvedItems, title, documentationUrl));
    }

    private static string? GetControllerName(string? controller)
        => controller?.EndsWith("Controller") is true ? controller[..^"Controller".Length] : controller;
}

public record BreadcrumbItem(
    object? Text,
    string? Url = null,
    string? Action = null,
    string? Controller = null,
    object? RouteValues = null,
    bool Active = false,
    string? TestId = null);

public record BreadcrumbViewModel(IReadOnlyList<BreadcrumbItem> Items, object? Title, string? DocumentationUrl);
