using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.Payment;
using NBXplorer;
using NBXplorer.Models;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public class StoreOnChainWalletsController : Controller
    {
        private StoreData Store => HttpContext.GetStoreData();
        private readonly IAuthorizationService _authorizationService;
        private readonly BTCPayWalletProvider _btcPayWalletProvider;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly WalletRepository _walletRepository;
        private readonly ExplorerClientProvider _explorerClientProvider;
        private readonly CssThemeManager _cssThemeManager;

        public StoreOnChainWalletsController(
            IAuthorizationService authorizationService,
            BTCPayWalletProvider btcPayWalletProvider,
            BTCPayNetworkProvider btcPayNetworkProvider,
            WalletRepository walletRepository,
            ExplorerClientProvider explorerClientProvider,
            CssThemeManager cssThemeManager)
        {
            _authorizationService = authorizationService;
            _btcPayWalletProvider = btcPayWalletProvider;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _walletRepository = walletRepository;
            _explorerClientProvider = explorerClientProvider;
            _cssThemeManager = cssThemeManager;
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet")]
        public async Task<IActionResult> ShowOnChainWalletOverview(string storeId, string cryptoCode)
        {
            if (IsValidWalletRequest(cryptoCode, out BTCPayNetwork network,
                out DerivationSchemeSettings derivationScheme, out IActionResult actionResult)) return actionResult;

            var wallet = _btcPayWalletProvider.GetWallet(network);
            return Ok(new OnChainWalletOverviewData()
            {
                Balance = await wallet.GetBalance(derivationScheme.AccountDerivation)
            });
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/transactions")]
        public async Task<IActionResult> ShowOnChainWalletTransactions(string storeId, string cryptoCode,
            TransactionStatus[] statusFilter = null)
        {
            if (IsValidWalletRequest(cryptoCode, out BTCPayNetwork network,
                out DerivationSchemeSettings derivationScheme, out IActionResult actionResult)) return actionResult;

            var wallet = _btcPayWalletProvider.GetWallet(network);
            var walletId = new WalletId(storeId, cryptoCode);
            var walletBlobAsync = await _walletRepository.GetWalletInfo(walletId);
            var walletTransactionsInfoAsync = await _walletRepository.GetWalletTransactionsInfo(walletId);

            var txs = await wallet.FetchTransactions(derivationScheme.AccountDerivation);
            var filteredFlatList = new List<TransactionInformation>();
            if (statusFilter is null || statusFilter.Contains(TransactionStatus.Confirmed))
            {
                filteredFlatList.AddRange(txs.ConfirmedTransactions.Transactions);
            }

            if (statusFilter is null || statusFilter.Contains(TransactionStatus.Unconfirmed))
            {
                filteredFlatList.AddRange(txs.UnconfirmedTransactions.Transactions);
            }

            if (statusFilter is null || statusFilter.Contains(TransactionStatus.Replaced))
            {
                filteredFlatList.AddRange(txs.ReplacedTransactions.Transactions);
            }


            return Ok(filteredFlatList.Select(information =>
            {
                walletTransactionsInfoAsync.TryGetValue(information.TransactionId.ToString(), out var transactionInfo);
                return ToModel(transactionInfo, information, wallet);
            }).ToList());
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/transactions/{transactionId}")]
        public async Task<IActionResult> GetOnChainWalletTransaction(string storeId, string cryptoCode,
            string transactionId)
        {
            if (IsValidWalletRequest(cryptoCode, out BTCPayNetwork network,
                out DerivationSchemeSettings derivationScheme, out IActionResult actionResult)) return actionResult;

            var wallet = _btcPayWalletProvider.GetWallet(network);
            var tx = await wallet.FetchTransaction(derivationScheme.AccountDerivation, uint256.Parse(transactionId));
            if (tx is null)
            {
                return NotFound();
            }

            var walletId = new WalletId(storeId, cryptoCode);
            var walletTransactionsInfoAsync =
                (await _walletRepository.GetWalletTransactionsInfo(walletId, new[] {transactionId})).Values
                .FirstOrDefault();

            return Ok(ToModel(walletTransactionsInfoAsync, tx, wallet));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/utxos")]
        public async Task<IActionResult> GetOnChainWalletUTXOs(string storeId, string cryptoCode)
        {
            if (IsValidWalletRequest(cryptoCode, out BTCPayNetwork network,
                out DerivationSchemeSettings derivationScheme, out IActionResult actionResult)) return actionResult;

            var wallet = _btcPayWalletProvider.GetWallet(network);

            var walletId = new WalletId(storeId, cryptoCode);
            var walletTransactionsInfoAsync = await _walletRepository.GetWalletTransactionsInfo(walletId);
            var utxos = await wallet.GetUnspentCoins(derivationScheme.AccountDerivation);
            return Ok(utxos.Select(coin =>
                {
                    walletTransactionsInfoAsync.TryGetValue(coin.OutPoint.Hash.ToString(), out var info);
                    return new OnChainWalletUTXOData()
                    {
                        Outpoint = coin.OutPoint.ToString(),
                        Amount = coin.Value.GetValue(network),
                        Comment = info?.Comment,
                        Labels = info?.Labels,
                        Link = string.Format(CultureInfo.InvariantCulture, network.BlockExplorerLink,
                            coin.OutPoint.Hash.ToString())
                    };
                }).ToList()
            );
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/transactions")]
        public async Task<IActionResult> CreateOnChainTransaction(string cryptoCode,
            [FromBody] CreateOnChainTransactionRequest request)
        {
            if (IsValidWalletRequest(cryptoCode, out BTCPayNetwork network,
                out DerivationSchemeSettings derivationScheme, out IActionResult actionResult)) return actionResult;
            if (network.ReadonlyWallet)
            {
                return this.CreateAPIError("not-available",
                    $"{cryptoCode} sending services are not currently available");
            }

            if (!(await CanUseHotWallet()).HotWallet)
            {
                return Unauthorized();
            }

            var wallet = _btcPayWalletProvider.GetWallet(network);

            var utxos = await wallet.GetUnspentCoins(derivationScheme.AccountDerivation);
            if (request.SelectedInputs?.Any() is true)
            {
                utxos = utxos.Where(coin => request.SelectedInputs.Contains(coin.OutPoint.ToString())).ToArray();
                if (utxos.Any() is false)
                {
                    //no valid utxos selected
                }
            }

            var balanceAvailable = utxos.Sum(coin => coin.Value.GetValue(network));
            var payjoinEndpoint = "";
            foreach (var destination in request.Destinations)
            {
                if (destination.Amount is null)
                {
                    try
                    {
                        var bip21 = new BitcoinUrlBuilder(destination.Destination, network.NBitcoinNetwork);
                        if (destination.SubtractFromAmount)
                        {
                            //cant subtract from amount if using bip21
                        }

                        //
                        // if (request.ProceedWithPayjoin && string.IsNullOrEmpty(payjoinEndpoint) && bip21.)
                        // {
                        //     
                        // }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        throw;
                    }
                }
            }

            return NotFound();
        }


        private async Task<(bool HotWallet, bool RPCImport)> CanUseHotWallet()
        {
            return await _authorizationService.CanUseHotWallet(_cssThemeManager.Policies, User);
        }


        private async Task<ExtKey> GetWallet(DerivationSchemeSettings derivationScheme)
        {
            if (!derivationScheme.IsHotWallet)
                return null;

            var result = await _explorerClientProvider.GetExplorerClient(derivationScheme.Network.CryptoCode)
                .GetMetadataAsync<string>(derivationScheme.AccountDerivation,
                    WellknownMetadataKeys.MasterHDKey);
            return string.IsNullOrEmpty(result) ? null : ExtKey.Parse(result, derivationScheme.Network.NBitcoinNetwork);
        }


        private bool IsValidWalletRequest(string cryptoCode, out BTCPayNetwork network,
            out DerivationSchemeSettings derivationScheme, out IActionResult actionResult)
        {
            derivationScheme = null;
            actionResult = null;
            network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (network is null)
            {
                {
                    actionResult = NotFound();
                    return true;
                }
            }

            if (!network.WalletSupported || !_btcPayWalletProvider.IsAvailable(network))
            {
                {
                    actionResult = this.CreateAPIError("not-available",
                        $"{cryptoCode} services are not currently available");
                    return true;
                }
            }

            derivationScheme = GetDerivationSchemeSettings(cryptoCode);
            if (derivationScheme?.AccountDerivation is null)
            {
                {
                    actionResult = NotFound();
                    return true;
                }
            }

            return false;
        }

        private DerivationSchemeSettings GetDerivationSchemeSettings(string cryptoCode)
        {
            var paymentMethod = Store
                .GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<DerivationSchemeSettings>()
                .FirstOrDefault(p =>
                    p.PaymentId.PaymentType == Payments.PaymentTypes.BTCLike &&
                    p.PaymentId.CryptoCode.Equals(cryptoCode, StringComparison.InvariantCultureIgnoreCase));
            return paymentMethod;
        }

        private OnChainWalletTransactionData ToModel(WalletTransactionInfo walletTransactionsInfoAsync,
            TransactionInformation tx,
            BTCPayWallet wallet)
        {
            return new OnChainWalletTransactionData()
            {
                Comment = walletTransactionsInfoAsync?.Comment,
                Labels = walletTransactionsInfoAsync?.Labels,
                Amount = tx.BalanceChange.GetValue(wallet.Network),
                BlockHash = tx.BlockHash,
                BlockHeight = tx.Height,
                Confirmations = tx.Confirmations,
                Timestamp = tx.Timestamp,
                Status = tx.Confirmations > 0 ? TransactionStatus.Confirmed :
                    tx.ReplacedBy != null ? TransactionStatus.Replaced : TransactionStatus.Unconfirmed
            };
        }
    }
}
