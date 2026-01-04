#nullable  enable

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Webhooks.Views;
using BTCPayServer.Services;

namespace BTCPayServer.Plugins.Webhooks;

public class WebhooksTranslationProvider(IEnumerable<AvailableWebhookViewModel> viewModels) : IDefaultTranslationProvider
{
    public Task<KeyValuePair<string, string?>[]> GetDefaultTranslations()
        => Task.FromResult(viewModels.Select(vm => KeyValuePair.Create(vm.Description, vm.Description)).ToArray())!;
}
