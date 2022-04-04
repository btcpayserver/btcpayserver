using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Services;

namespace BTCPayServer.Services;

public class LightningAddressStoreDeletedAction : PluginAction<string>
{
    private readonly ISettingsRepository _settingsRepository;

    public LightningAddressStoreDeletedAction(ISettingsRepository settingsRepository)
    {
        _settingsRepository = settingsRepository;
    }
    
    public override string Hook => "store-deleted";

    public override async Task Execute(string arg)
    {
        var settings = await UILNURLController.GetSettings(_settingsRepository);
        if (settings.StoreToItemMap.TryGetValue(arg, out var items))
        {
            settings.StoreToItemMap.Remove(arg,out _);
            foreach (string item in items)
            {
                settings.Items.Remove(item, out _);
            }

            await UILNURLController.SetSettings(_settingsRepository, settings);
        }
        
    }
}
