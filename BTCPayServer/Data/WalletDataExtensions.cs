using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BTCPayServer.Data
{
    public static class WalletDataExtensions
    {
        public static WalletBlobInfo GetBlobInfo(this WalletData walletData)
        {
            if (walletData.Blob == null || walletData.Blob.Length == 0)
            {
                return new WalletBlobInfo();
            }
            var blobInfo = JsonConvert.DeserializeObject<WalletBlobInfo>(ZipUtils.Unzip(walletData.Blob));
            return blobInfo;
        }
        public static void SetBlobInfo(this WalletData walletData, WalletBlobInfo blobInfo)
        {
            if (blobInfo == null)
            {
                walletData.Blob = Array.Empty<byte>();
                return;
            }
            walletData.Blob = ZipUtils.Zip(JsonConvert.SerializeObject(blobInfo));
        }
    }
}
