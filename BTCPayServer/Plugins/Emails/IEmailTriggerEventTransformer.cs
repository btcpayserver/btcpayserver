using BTCPayServer.Data;
using BTCPayServer.Plugins.Emails.HostedServices;

namespace BTCPayServer.Plugins.Emails;

public interface IEmailTriggerEventTransformer
{
    public class Context(TriggerEvent triggerEvent, StoreData store)
    {
        public TriggerEvent TriggerEvent { get; } = triggerEvent;
        public StoreData Store { get; } = store;
    }

    void Transform(Context context);
}
