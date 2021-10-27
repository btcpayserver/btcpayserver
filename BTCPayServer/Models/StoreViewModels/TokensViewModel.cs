using System.ComponentModel.DataAnnotations;
using BTCPayServer.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.StoreViewModels
{
    public class CreateTokenViewModel
    {
        [Display(Name = "Public Key")]
        [PubKeyValidatorAttribute]
        public string PublicKey
        {
            get; set;
        }

        public string Label
        {
            get; set;
        }

        [Required]
        public string StoreId
        {
            get; set;
        }

        [Display(Name = "Store")]
        public SelectList Stores
        {
            get; set;
        }
    }
    public class TokenViewModel
    {
        public string Id
        {
            get; set;
        }
        public string Label
        {
            get; set;
        }
        public string SIN
        {
            get; set;
        }
    }
    public class TokensViewModel
    {
        public TokenViewModel[] Tokens
        {
            get; set;
        }

        [Display(Name = "API Key")]
        public string ApiKey { get; set; }
        public string EncodedApiKey { get; set; }
        public bool StoreNotConfigured { get; set; }
    }
}
