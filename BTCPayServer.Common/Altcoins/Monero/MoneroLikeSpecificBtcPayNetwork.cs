namespace BTCPayServer
{
    public class MoneroLikeSpecificBtcPayNetwork : BTCPayNetworkBase
    {
        public int MaxTrackedConfirmation = 10;
        public override int Divisibility { get; set; } = 12;
    }
}
