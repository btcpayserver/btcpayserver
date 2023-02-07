using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NBitcoin;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletPSBTCombineViewModel
    {
        public string OtherPSBT { get; set; }
        [Display(Name = "PSBT to combine with...")]
        public string PSBT { get; set; }
        [Display(Name = "Upload PSBT from file...")]
        public IFormFile UploadedPSBTFile { get; set; }

        public string BackUrl { get; set; }
        public string ReturnUrl { get; set; }

        public PSBT GetSourcePSBT(Network network)
        {
            if (!string.IsNullOrEmpty(OtherPSBT))
            {
                try
                {
                    return NBitcoin.PSBT.Parse(OtherPSBT, network);
                }
                catch
                { }
            }
            return null;
        }
        public async Task<PSBT> GetPSBT(Network network)
        {
            if (UploadedPSBTFile != null)
            {
                if (UploadedPSBTFile.Length > 500 * 1024)
                    return null;
                byte[] bytes = new byte[UploadedPSBTFile.Length];
                using (var stream = UploadedPSBTFile.OpenReadStream())
                {
                    await stream.ReadAsync(bytes, 0, (int)UploadedPSBTFile.Length);
                }
                try
                {
                    return NBitcoin.PSBT.Load(bytes, network);
                }
                catch
                {
                    return null;
                }
            }
            if (!string.IsNullOrEmpty(PSBT))
            {
                try
                {
                    return NBitcoin.PSBT.Parse(PSBT, network);
                }
                catch
                { }
            }
            return null;
        }
    }
}
