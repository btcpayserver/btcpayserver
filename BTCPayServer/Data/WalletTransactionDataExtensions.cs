using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public static class WalletTransactionDataExtensions
    {
        public static WalletTransactionInfo GetBlobInfo(this WalletTransactionData walletTransactionData)
        {
            if (walletTransactionData.Blob == null || walletTransactionData.Blob.Length == 0)
            {
                return new WalletTransactionInfo();
            }
            var blobInfo = JsonConvert.DeserializeObject<WalletTransactionInfo>(ZipUtils.Unzip(walletTransactionData.Blob));
            if (!string.IsNullOrEmpty(walletTransactionData.Labels))
            {
                if (walletTransactionData.Labels.StartsWith('['))
                {
                    blobInfo.Labels.AddRange(JArray.Parse(walletTransactionData.Labels).Values<string>());
                }
                else
                {
                    blobInfo.Labels.AddRange(walletTransactionData.Labels.Split(',',
                        StringSplitOptions.RemoveEmptyEntries));
                }
            }
            return blobInfo;
        }
        public static void SetBlobInfo(this WalletTransactionData walletTransactionData, WalletTransactionInfo blobInfo)
        {
            if (blobInfo == null)
            {
                walletTransactionData.Labels = string.Empty;
                walletTransactionData.Blob = Array.Empty<byte>();
                return;
            }

            walletTransactionData.Labels = JArray.FromObject(blobInfo.Labels).ToString();
            walletTransactionData.Blob = ZipUtils.Zip(JsonConvert.SerializeObject(blobInfo));
        }
    }
}
