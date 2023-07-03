#nullable enable
using System.Linq;
using System.Threading.Tasks;
using BTCPayApp.CommonServer;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Shopify;
using BTCPayServer.Security.Greenfield;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer;

namespace BTCPayServer.Controllers;

[Route("btcpayapp")]
public class BtcPayAppController : Controller
{
    private readonly BtcPayAppService _appService;
    private readonly APIKeyRepository _apiKeyRepository;
    private readonly StoreRepository _storeRepository;
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    private readonly ExplorerClientProvider _explorerClientProvider;

    public BtcPayAppController(BtcPayAppService appService, APIKeyRepository apiKeyRepository,
        StoreRepository storeRepository, BTCPayNetworkProvider btcPayNetworkProvider,
        ExplorerClientProvider explorerClientProvider)
    {
        _appService = appService;
        _apiKeyRepository = apiKeyRepository;
        _storeRepository = storeRepository;
        _btcPayNetworkProvider = btcPayNetworkProvider;
        _explorerClientProvider = explorerClientProvider;
    }

    [HttpGet("pair/{code}")]
    public async Task<IActionResult> StartPair(string code)
    {
        var res = _appService.ConsumePairingCode(code);
        if (res is null)
        {
            return Unauthorized();
        }

        var store = await _storeRepository.FindStore(res.StoreId, res.UserId);
        if (store is null)
        {
            return NotFound();
        }

        var key = new APIKeyData()
        {
            Id = Encoders.Hex.EncodeData(RandomUtils.GetBytes(20)),
            Type = APIKeyType.Permanent,
            UserId = res.UserId,
            Label = "BTCPay App Pairing"
        };
        key.SetBlob(new APIKeyBlob() {Permissions = new[] {Policies.Unrestricted}});
        await _apiKeyRepository.CreateKey(key);


        var onchain = store.GetDerivationSchemeSettings(_btcPayNetworkProvider, "BTC");
        string? onchainSeed = null;
        if (onchain is not null)
        {
            var explorerClient = _explorerClientProvider.GetExplorerClient("BTC");
            onchainSeed = await GetSeed(explorerClient, onchain);
        }

        return Ok(new PairSuccessResult()
        {
            Key = key.Id,
            StoreId = store.Id,
            UserId = res.UserId,
            ExistingWallet =
                onchain?.AccountDerivation?.GetExtPubKeys()?.FirstOrDefault()
                    ?.ToString(onchain.Network.NBitcoinNetwork),
            ExistingWalletSeed = onchainSeed,
            Network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>("BTC").NBitcoinNetwork.Name
        });
    }

    private async Task<string?> GetSeed(ExplorerClient client, DerivationSchemeSettings derivation)
    {
        return derivation.IsHotWallet &&
               await client.GetMetadataAsync<string>(derivation.AccountDerivation, WellknownMetadataKeys.Mnemonic) is
                   { } seed &&
               !string.IsNullOrEmpty(seed)
            ? seed
            : null;
    }
}
