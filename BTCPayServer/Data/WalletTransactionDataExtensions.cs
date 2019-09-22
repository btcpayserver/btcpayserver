using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

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
                blobInfo.Labels.AddRange(walletTransactionData.Labels.Split(',', StringSplitOptions.RemoveEmptyEntries));
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
            if (blobInfo.Labels.Any(l => l.Contains(',', StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException(paramName: nameof(blobInfo), message: "Labels must not contains ','");
            walletTransactionData.Labels = String.Join(',', blobInfo.Labels);
            walletTransactionData.Blob = ZipUtils.Zip(JsonConvert.SerializeObject(blobInfo));
        }
    }
}
