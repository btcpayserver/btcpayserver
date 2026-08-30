#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Plugins.GlobalSearch;
using BTCPayServer.Plugins.GlobalSearch.Views;
using BTCPayServer.Plugins.Maintenance.Controllers;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Maintenance;

public class MaintenanceSearchResultProvider(CheckHostCommandsHostedService hostCommands) : ISearchResultItemProvider
{
    private static readonly string[] Keywords = ["Server", "Settings", "Maintenance"];

    public Task ProvideAsync(SearchResultItemProviderContext context, CancellationToken cancellationToken)
    {
        if (context.UserQuery is not null || !hostCommands.BTCPayHostAvailable)
            return Task.CompletedTask;

        context.ItemResults.Add(new ResultItemViewModel
        {
            RequiredPolicy = Policies.CanModifyServerSettings,
            Title = "Go to the maintenance page",
            Url = context.Url.Action(nameof(UIMaintenanceController.Maintenance), "UIMaintenance", new { area = MaintenancePlugin.Area }),
            Category = "Server",
            Keywords = Keywords
        });
        if (hostCommands.SupportedCommands.Contains(HostCommands.Update))
        {
            context.ItemResults.Add(new ResultItemViewModel
            {
                RequiredPolicy = Policies.CanModifyServerSettings,
                Title = "Update the server",
                Url = context.Url.Action(nameof(UIMaintenanceController.Maintenance), "UIMaintenance", new { area = MaintenancePlugin.Area }),
                Category = "Server",
                Keywords = Keywords
            });
        }
        return Task.CompletedTask;
    }

    internal class TranslationProvider : IDefaultTranslationProvider
    {
        public Task<KeyValuePair<string, string?>[]> GetDefaultTranslations()
        {
            return Task.FromResult<KeyValuePair<string, string?>[]>([
                KeyValuePair.Create("Go to the maintenance page", null as string),
                KeyValuePair.Create("Update the server", null as string),
                KeyValuePair.Create("Server", null as string),
                KeyValuePair.Create("Settings", null as string),
                KeyValuePair.Create("Maintenance", null as string)
            ]);
        }
    }
}
