using BTCPayServer.Data;
using NBitcoin;
using NBXplorer;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.CoinSwitch
{
    public static class CoinSwitchExtensions
    {
        public const string StoreBlobKey = "coinSwitchSettings";
        public static CoinSwitchSettings GetCoinSwitchSettings(this StoreBlob storeBlob)
        {
            if (storeBlob.AdditionalData.TryGetValue(StoreBlobKey, out var rawS) && rawS is JObject rawObj)
            {
                return new Serializer(null).ToObject<CoinSwitchSettings>(rawObj);
            }

            return null;
        }
        public static void SetCoinSwitchSettings(this StoreBlob storeBlob, CoinSwitchSettings settings)
        {
            if (settings is null)
            {
                storeBlob.AdditionalData.Remove(StoreBlobKey);
            }
            else
            {
                storeBlob.AdditionalData.AddOrReplace(StoreBlobKey, JObject.FromObject(settings));
            }
        }
    }
}
