#if ALTCOINS
using System;

namespace BTCPayServer.Services.Altcoins.Ethereum.UI
{
    public class EthereumPaymentViewModel
    {
        public string Crypto { get; set; }
        public string Confirmations { get; set; }
        public string DepositAddress { get; set; }
        public string Amount { get; set; }
        public DateTimeOffset ReceivedTime { get; set; }
        public long? BlockNumber { get; set; }
        public string BalanceLink { get; set; }
        public bool Replaced { get; set; }
        public long Index { get; set; }
        public string Sweep { get; set; }
    }
}
#endif
