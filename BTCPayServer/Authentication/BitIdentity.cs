using NBitcoin;
using NBitcoin.DataEncoders;
using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Text;

namespace BTCPayServer.Authentication
{
    public class BitIdentity : IIdentity
    {
        public BitIdentity(PubKey key)
        {
            PubKey = key;
            _Name = Encoders.Base58Check.EncodeData(Encoders.Hex.DecodeData("0f02" + key.Hash.ToString()));
            SIN = NBitpayClient.Extensions.BitIdExtensions.GetBitIDSIN(key);
        }
        string _Name;

        public string SIN
        {
            get;
        }
        public PubKey PubKey
        {
            get;
        }

        public string AuthenticationType => "BitID";

        public bool IsAuthenticated => true;

        public string Name => _Name;
    }
}
