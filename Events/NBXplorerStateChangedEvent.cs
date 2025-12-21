using BTCPayServer.HostedServices;

namespace BTCPayServer.Events
{
    public class NBXplorerStateChangedEvent
    {
        public NBXplorerStateChangedEvent(BTCPayNetworkBase network, NBXplorerState old, NBXplorerState newState)
        {
            Network = network;
            NewState = newState;
            OldState = old;
        }

        public BTCPayNetworkBase Network { get; set; }
        public NBXplorerState NewState { get; set; }
        public NBXplorerState OldState { get; set; }

        public override string ToString()
        {
            return $"NBXplorer {Network.CryptoCode}: {OldState} => {NewState}";
        }
    }
}
