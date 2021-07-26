using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletPSBTExportViewModel : WalletPSBTReadyViewModel
    {
        public string CryptoCode { get; set; }
        public string PSBTHex { get; set; }
        public string PSBT { get; set; }

        [Display(Name = "Upload PSBT from file")]
        public IFormFile UploadedPSBTFile { get; set; }
    }
}
