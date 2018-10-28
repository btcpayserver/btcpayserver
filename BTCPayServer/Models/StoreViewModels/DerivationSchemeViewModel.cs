using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using NBitcoin;

namespace BTCPayServer.Models.StoreViewModels
{
    public class DerivationSchemeViewModel
    {
        private string _derivationScheme;

        public DerivationSchemeViewModel()
        {
        }

        public string DerivationScheme
        {
            get => _derivationScheme;
            set
            {
                _derivationScheme = value;
                var split = _derivationScheme.Split("-");
                ExtPubKey = split[0];
                if (split.Length <= 1) return;
                var suffix = "";
                if (split.Length > 1)
                {
                    suffix = "-" + split[1];
                }

                var type = AddressTypes.SingleOrDefault(addressType => addressType.Suffix == suffix);
                AddressType = type?.Type;
            }
        }


        public List<(string KeyPath, string Address)> AddressSamples { get; set; } =
            new List<(string KeyPath, string Address)>();

        public string CryptoCode { get; set; }
        [Display(Name = "Hint address")] public string HintAddress { get; set; }
        public bool Confirmation { get; set; }
        public bool Enabled { get; set; } = true;

        public string ServerUrl { get; set; }
        public string StatusMessage { get; internal set; }
        public KeyPath RootKeyPath { get; set; }

        [Display(Name = "xpub / ypub key")]
        public string ExtPubKey { get; set; }
        public string AddressType { get; set; }


        public IEnumerable<AddressType> AddressTypes = new List<AddressType>()
        {
            new AddressType()
            {
                Suffix = "",
                Type = "P2WPKH / Multi-sig P2WSH"
            },
            new AddressType()
            {
                Suffix = "-[p2sh]",
                Type = "P2SH-P2WPKH / Multi-sig P2SH-P2WSH"
            },
            new AddressType()
            {
                Suffix = "-[legacy]",
                Type = "P2PKH / Multi-sig P2SH"
            }
        };
    }

    public class AddressType
    {
        public string Type { get; set; }
        public string Suffix { get; set; }
    }
}
