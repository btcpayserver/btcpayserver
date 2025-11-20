using BTCPayServer.Plugins.Emails.Views;
namespace BTCPayServer.Plugins.Emails;

public interface IEmailTriggerViewModelTransformer
{
    void Transform(EmailTriggerViewModel viewModel);
}
