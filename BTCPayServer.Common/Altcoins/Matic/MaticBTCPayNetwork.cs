#if ALTCOINS
namespace BTCPayServer
{
    public class MaticBTCPayNetwork : BTCPayNetworkBase
    {
        public int ChainId { get; set; }
        public int CoinType { get; set; }

        public string GetDefaultKeyPath()
        {
            return $"m/44'/{CoinType}'/0'/0/x";
        }
    }

    public class ERC20MaticBTCPayNetwork : MaticBTCPayNetwork
    {
        public string SmartContractAddress { get; set; }
    }
}
#endif
