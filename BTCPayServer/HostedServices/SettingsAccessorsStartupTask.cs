#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Events;
using BTCPayServer.Services;

namespace BTCPayServer.HostedServices;

public class SettingsAccessorsStartupTask(
    IEnumerable<SettingsAccessorsStartupTask.Registration> registrations,
    SettingsRepository settingsRepository,
    EventAggregator eventAggregator,
    IServiceProvider serviceProvider)
: IStartupTask
{
    public record Registration(Type SettingType, string? KeyName = null);

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var settingsAccessorByTypes = registrations
            .Select(r => (r, (SettingsAccessor)serviceProvider.GetService(typeof(ISettingsAccessor<>).MakeGenericType(r.SettingType))!))
            .ToDictionary(k => k.r.SettingType, k => (KeyName: k.r.KeyName, Accessor: k.Item2));
        var values = await settingsRepository.LoadAllSettings(settingsAccessorByTypes.Select(r => (r.Key, r.Value.KeyName)).ToArray());
        foreach (var o in settingsAccessorByTypes)
        {
            o.Value.Accessor.SetSetting(values[o.Key]);
            var settingsChangedType = typeof(SettingsChanged<>).MakeGenericType(o.Key);
            // We could dispose the subscriptions explicitely when the server stop but EventAggregator dispose anyway when the service quit
            eventAggregator.Subscribe(settingsChangedType, (s, oo) => o.Value.Accessor.SetSetting(settingsChangedType.GetProperty(nameof(SettingsChanged<object>.Settings))!.GetValue(oo)));
        }
    }
}
