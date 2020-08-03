using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NBitcoin;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletPSBTViewModel
    {
        public string CryptoCode { get; set; }
        public string Decoded { get; set; }
        string _FileName;
        public bool NBXSeedAvailable { get; set; }

        public string FileName
        {
            get
            {
                return string.IsNullOrEmpty(_FileName) ? "psbt-export.psbt" : _FileName;
            }
            set
            {
                _FileName = value;
            }
        }
        [Display(Name = "PSBT content")]
        public string PSBT { get; set; }
        public List<string> Errors { get; set; } = new List<string>();

        [Display(Name = "Upload PSBT from file...")]
        public IFormFile UploadedPSBTFile { get; set; }

        public async Task<PSBT> GetPSBT(Network network)
        {
            if (UploadedPSBTFile != null)
            {
                if (UploadedPSBTFile.Length > 500 * 1024)
                    return null;

                try
                {
                    byte[] bytes = new byte[UploadedPSBTFile.Length];
                    await using (var stream = UploadedPSBTFile.OpenReadStream())
                    {
                        await stream.ReadAsync(bytes, 0, (int)UploadedPSBTFile.Length);
                    }
                    return NBitcoin.PSBT.Load(bytes, network);
                }
                catch (Exception)
                {
                    using var stream = new StreamReader(UploadedPSBTFile.OpenReadStream());
                    PSBT = await stream.ReadToEndAsync();
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
