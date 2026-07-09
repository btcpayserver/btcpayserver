#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Plugins.Wallets.Views.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.LabelSelector;

[ViewComponent]
public class LabelSelector : ViewComponent
{
    public const int MaxVisibleLabels = 20;


    public IViewComponentResult Invoke(
        SearchString search,
        bool allowNoLabelFilter = false,
        IEnumerable<LabelSelectorItemViewModel>? labels = null)
    {
        var allLabels = (labels ?? new List<LabelSelectorItemViewModel>())
            .OrderBy(label => label.Text, StringComparer.OrdinalIgnoreCase)
            .Select(label => new LabelSelectorItemViewModel
            {
                Text = label.Text,
                Color = label.Color,
                TextColor = label.TextColor,
                UsageCount = label.UsageCount
            })
            .ToList();
        var popular =
            allLabels
            .OrderByDescending(c => c.UsageCount)
            .ThenBy(c => c.Text, StringComparer.OrdinalIgnoreCase)
            .Take(MaxVisibleLabels)
            .OrderBy(c => c.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return View(new LabelSelectorModel
        {
            Search = search,
            Labels = allLabels,
            InitialLabels = popular,
            AllowNoLabelFilter = allowNoLabelFilter
        });
    }
    public class LabelSelectorModel
    {
        public required SearchString Search { get; init; }
        public required List<LabelSelectorItemViewModel> Labels { get; init; }
        public required List<LabelSelectorItemViewModel> InitialLabels { get; init; }
        public string[] ActiveLabels => Search.GetFilterArray("label") ?? [];
        public bool HasNoLabelFilter => Search.GetFilterBool("nolabel") is true;
        public int LabelFilterCount => ActiveLabels.Length + (HasNoLabelFilter ? 1 : 0);
        public bool AllowNoLabelFilter { get; init; }
    }

    public static void RunFilterCommand(SearchString search, string filterCommand)
    {
        if (filterCommand is "alllabels")
        {
            search.Filters.Remove("label");
            search.Filters.Remove("nolabel");
        }
        else if (filterCommand is "nolabel")
        {
            search.SetFilter("nolabel", "true");
            search.Filters.Remove("label");
        }
        else if (filterCommand.StartsWith("addlabel:"))
        {
            search.Filters.Remove("nolabel");
            search.SetFilter("label", filterCommand.Substring("addlabel:".Length), toggle: true, multi: true);
        }
    }

    public static void AddUIFilters(SearchString search)
    {
        foreach (var filter in new[]{"label", "nolabel"})
            search.UIFilterTypes.Add(filter);
    }
}

public class LabelSelectorItemViewModel
{
    public required string Text { get; init; }
    public required string Color { get; init; }
    public required string TextColor { get; init; }
    public long UsageCount { get; init; }
}
