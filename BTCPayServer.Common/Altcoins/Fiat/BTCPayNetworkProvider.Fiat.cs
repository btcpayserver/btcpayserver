using BTCPayServer.Common.Altcoins.Fiat;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitFiat()
        {
            var supportedFiat = new string[] {"USD", "EUR", "JPY"};
            foreach (string fiat in supportedFiat)
            {
                Add(new FiatPayNetwork()
                {
                    CryptoCode = fiat,
                    Divisibility = 2,
                    DisplayName = fiat,
                    ShowSyncSummary = false
                     
                });
            }
            
        }
    }
}
