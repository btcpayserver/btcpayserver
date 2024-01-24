#nullable enable
using System;
using System.Collections.Generic;
using NBitcoin.DataEncoders;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BTCPayServer.Services.WalletFileParsing;

namespace BTCPayServer.Services;

public class WalletFileParsers(IEnumerable<IWalletFileParser> parsers)
{
    public HashSet<string> GetSourceHandles()
    {
        return parsers.Aggregate(new HashSet<string>(), (res, p) =>
        {
            res.UnionWith(p.SourceHandles);
            return res;
        });
    }
    
    public bool TryParseWalletFile(string fileContents, BTCPayNetwork network, [MaybeNullWhen(false)] out DerivationSchemeSettings settings, [MaybeNullWhen(true)] out string error)
    {
        return TryParse(fileContents, parsers, network, out settings, out error);
    }

    public bool TryParseWalletFile(string fileContents, string source, BTCPayNetwork network, [MaybeNullWhen(false)] out DerivationSchemeSettings settings, [MaybeNullWhen(true)] out string error)
    {
        ArgumentNullException.ThrowIfNull(source);
        var sourceParsers = parsers.Where(p => p.SourceHandles.Contains(source));
        return TryParse(fileContents, sourceParsers, network, out settings, out error);
    }

    private bool TryParse(string fileContents, IEnumerable<IWalletFileParser> fileParsers, BTCPayNetwork network, [MaybeNullWhen(false)] out DerivationSchemeSettings settings, [MaybeNullWhen(true)] out string error)
    {
        settings = null;
        error = null;
        ArgumentNullException.ThrowIfNull(fileContents);
        ArgumentNullException.ThrowIfNull(network);
        if (HexEncoder.IsWellFormed(fileContents))
        {
            fileContents = Encoding.UTF8.GetString(Encoders.Hex.DecodeData(fileContents));
        }

        foreach (IWalletFileParser parser in fileParsers)
        {
            try
            {
                if (parser.TryParse(network, fileContents, out settings, out error))
                    return true;
            }
            catch (Exception e)
            {
                error = e.Message;
            }
        }
        error ??= "Unsupported file format";
        return false;
    }
}
