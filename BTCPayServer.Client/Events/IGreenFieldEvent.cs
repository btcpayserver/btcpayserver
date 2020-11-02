using NBitcoin;

namespace BTCPayServer.Client.Events
{
    public interface IGreenFieldEvent
    {
        string EventType { get; set; }
        object Payload { get; set; }
        string Signature { get; set; }
        void SetSignature(string url, Key key);
        bool VerifySignature(string url, PubKey key);
    }
}
