#nullable enable
using System;
using System.Collections.Generic;
using NBitcoin.DataEncoders;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using BTCPayServer.Services.WalletFileParsing;

namespace BTCPayServer.Services;

public class WalletFileParsers
{
    public WalletFileParsers(IEnumerable<IWalletFileParser> parsers)
    {
        Parsers = parsers;
    }
    public IEnumerable<IWalletFileParser> Parsers { get; }

    public bool TryParseWalletFile(string fileContents, BTCPayNetwork network, [MaybeNullWhen(false)] out DerivationSchemeSettings settings, [MaybeNullWhen(true)] out string error)
    {
        settings = null;
        error = null;
        ArgumentNullException.ThrowIfNull(fileContents);
        ArgumentNullException.ThrowIfNull(network);
        if (HexEncoder.IsWellFormed(fileContents))
        {
            fileContents = Encoding.UTF8.GetString(Encoders.Hex.DecodeData(fileContents));
        }

        foreach (IWalletFileParser onChainWalletParser in Parsers)
        {
            try
            {
                if (onChainWalletParser.TryParse(network, fileContents, out settings))
                    return true;
            }
            catch (Exception)
            {
            }
        }
        error = "Unsupported file format";
        return false;
    }
}
