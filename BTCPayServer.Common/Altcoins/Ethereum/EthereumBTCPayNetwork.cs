#if ALTCOINS
namespace BTCPayServer
{
    public class EthereumBTCPayNetwork : BTCPayNetworkBase
    {
        public int ChainId { get; set; }
        public int CoinType { get; set; }

        public string GetDefaultKeyPath()
        {
            return $"m/44'/{CoinType}'/0'/0/x";
        }
    }

    public class ERC20BTCPayNetwork : EthereumBTCPayNetwork
    {
        public string SmartContractAddress { get; set; }
    }
}
#endif
