using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BTCPayServer.Data
{
    public class WalletTransactionData
    {
        public string WalletDataId { get; set; }
        public WalletData WalletData { get; set; }
        public string TransactionId { get; set; }
        public string Labels { get; set; }
        public byte[] Blob { get; set; }

        public WalletTransactionInfo GetBlobInfo()
        {
            if (Blob == null || Blob.Length == 0)
            {
                return new WalletTransactionInfo();
            }
            var blobInfo = JsonConvert.DeserializeObject<WalletTransactionInfo>(ZipUtils.Unzip(Blob));
            if (!string.IsNullOrEmpty(Labels))
            {
                blobInfo.Labels.AddRange(Labels.Split(',', StringSplitOptions.RemoveEmptyEntries));
            }
            return blobInfo;
        }
        public void SetBlobInfo(WalletTransactionInfo blobInfo)
        {
            if (blobInfo == null)
            {
                Labels = string.Empty;
                Blob = Array.Empty<byte>();
                return;
            }
            if (blobInfo.Labels.Any(l => l.Contains(',', StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException(paramName: nameof(blobInfo), message: "Labels must not contains ','");
            Labels = String.Join(',', blobInfo.Labels);
            Blob = ZipUtils.Zip(JsonConvert.SerializeObject(blobInfo));
        }
    }

    public class WalletTransactionInfo
    {
        public string Comment { get; set; } = string.Empty;
        [JsonIgnore]
        public HashSet<string> Labels { get; set; } = new HashSet<string>();
    }
}
