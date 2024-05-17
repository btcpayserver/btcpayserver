using System.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Services.Altcoins.Litecoin
{
    public class MwebBech32Encoder()
        : Bech32Encoder(Encoders.ASCII.DecodeData("ltcmweb"))
    {
        public override byte[] Decode(string addr, out byte witnessVerion)
        {
            StrictLength = false;
            var data = DecodeDataCore(addr, out _);
            witnessVerion = data[0];
            return ConvertBits(data.Skip(1), 5, 8, false);
        }
    }

    public class BitcoinMwebAddress(string address, Network network)
        : BitcoinAddress(address, network)
    {
        protected override Script GeneratePaymentScript()
        {
            var bech32 = new MwebBech32Encoder();
            return new Script(bech32.Decode(_Str, out _));
        }
    }
}
