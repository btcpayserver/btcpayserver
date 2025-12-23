#nullable  enable

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Services;

namespace BTCPayServer.Plugins.Emails;

public class EmailsTranslationProvider(EmailTriggerViewModels viewModels) : IDefaultTranslationProvider
{
    public Task<KeyValuePair<string, string?>[]> GetDefaultTranslations()
        => Task.FromResult(
            viewModels.GetViewModels().Select(vm => KeyValuePair.Create(vm.Description, vm.Description))
                .Concat(viewModels.GetViewModels().SelectMany(vm => vm.PlaceHolders).Select(p => KeyValuePair.Create(p.Description, p.Description)))
                .ToArray())!;
}
