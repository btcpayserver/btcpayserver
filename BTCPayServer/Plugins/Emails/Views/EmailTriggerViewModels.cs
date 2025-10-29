#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace BTCPayServer.Plugins.Emails.Views;

public class EmailTriggerViewModels(
    IEnumerable<IEmailTriggerViewModelTransformer> transformers,
    IEnumerable<EmailTriggerViewModel> registeredTriggers)
{
    public class Context(EmailTriggerViewModel viewModel)
    {
        public EmailTriggerViewModel ViewModel { get; } = viewModel;
    }

    public List<EmailTriggerViewModel> GetViewModels()
        => registeredTriggers
            .Select(t => t.Clone())
            .Select(Transform)
            .ToList();

    private EmailTriggerViewModel Transform(EmailTriggerViewModel arg)
    {
        foreach (var transformer in transformers)
        {
            transformer.Transform(arg);
        }
        return arg;
    }
}
