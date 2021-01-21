using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer.Models;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public class StoreOnChainWalletsController : Controller
    {
        private StoreData Store => HttpContext.GetStoreData();
        private readonly BTCPayWalletProvider _btcPayWalletProvider;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly WalletRepository _walletRepository;
        private readonly LabelFactory _labelFactory;

        public StoreOnChainWalletsController(BTCPayWalletProvider btcPayWalletProvider,
            BTCPayNetworkProvider btcPayNetworkProvider, WalletRepository walletRepository, LabelFactory labelFactory)
        {
            _btcPayWalletProvider = btcPayWalletProvider;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _walletRepository = walletRepository;
            _labelFactory = labelFactory;
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
            var walletBlobAsync = await _walletRepository.GetWalletInfo(walletId);
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
                        // Labels =
                        //     info == null
                        //         ? null
                        //         :  _labelFactory.ColorizeTransactionLabels(walletBlobAsync, info, Request),
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

            return NotFound();
        }

        public class CreateOnChainTransactionRequest
        {
            public class CreateOnChainTransactionRequestDestination
            {
                public string Destination { get; set; }
                public decimal Amount { get; set; }
                public bool SubtractFromAmount { get; set; }
            }

            public decimal? FeeSatoshiPerByte { get; set; }
            public bool ProceedWithPayjoin { get; set; }
            public bool NoChange { get; set; }
            public IEnumerable<string> SelectedInputs { get; set; }
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
