#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace BTCPayServer.Services.WalletFileParsing;
public interface IWalletFileParser
{
    bool TryParse(BTCPayNetwork network, string data, [MaybeNullWhen(false)] out DerivationSchemeSettings derivationSchemeSettings);
}
