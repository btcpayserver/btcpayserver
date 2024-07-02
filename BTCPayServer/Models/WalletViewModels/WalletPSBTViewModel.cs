using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NBitcoin;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletPSBTViewModel : WalletPSBTReadyViewModel
    {
        public string CryptoCode { get; set; }
        public string Decoded { get; set; }
        string _FileName;
        public string PSBTHex { get; set; }
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

        [Display(Name = "Upload PSBT from file…")]
        public IFormFile UploadedPSBTFile { get; set; }


        public async Task<PSBT> GetPSBT(Network network, ModelStateDictionary modelState)
        {
            var psbt = await GetPSBTCore(network, modelState);
            if (psbt != null)
            {
                Decoded = psbt.ToString();
                PSBTHex = psbt.ToHex();
                PSBT = psbt.ToBase64();
                if (SigningContext is null)
                    SigningContext = new SigningContextModel(psbt);
                else
                    SigningContext.PSBT = psbt.ToBase64();
            }
            return psbt;
        }
        public bool InvalidPSBT { get; set; }

        async Task<PSBT> GetPSBTCore(Network network, ModelStateDictionary modelState)
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
                catch (Exception ex)
                {
                    using var stream = new StreamReader(UploadedPSBTFile.OpenReadStream());
                    PSBT = await stream.ReadToEndAsync();
                    modelState.Remove(nameof(PSBT));
                    modelState.AddModelError(nameof(PSBT), ex.Message);
                    InvalidPSBT = true;
                }
            }
            if (SigningContext != null && !string.IsNullOrEmpty(SigningContext.PSBT))
            {
                PSBT = SigningContext.PSBT;
                modelState.Remove(nameof(PSBT));
                InvalidPSBT = false;
            }
            if (!string.IsNullOrEmpty(PSBT))
            {
                try
                {
                    InvalidPSBT = false;
                    return NBitcoin.PSBT.Parse(PSBT, network);
                }
                catch (Exception ex) when (!InvalidPSBT)
                {
                    modelState.AddModelError(nameof(PSBT), ex.Message);
                    InvalidPSBT = true;
                }
            }
            return null;
        }
    }
}
