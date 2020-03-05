using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer;
using NBXplorer.Models;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Payments.PayJoin
{
    [Route("{cryptoCode}/bpu")]
    public class PayJoinEndpointController : ControllerBase
    {
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly ExplorerClientProvider _explorerClientProvider;
        private readonly StoreRepository _storeRepository;
        private readonly BTCPayWalletProvider _btcPayWalletProvider;
        private readonly PayJoinStateProvider _payJoinStateProvider;

        public PayJoinEndpointController(BTCPayNetworkProvider btcPayNetworkProvider,
            InvoiceRepository invoiceRepository, ExplorerClientProvider explorerClientProvider,
            StoreRepository storeRepository, BTCPayWalletProvider btcPayWalletProvider,
            PayJoinStateProvider payJoinStateProvider)
        {
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _invoiceRepository = invoiceRepository;
            _explorerClientProvider = explorerClientProvider;
            _storeRepository = storeRepository;
            _btcPayWalletProvider = btcPayWalletProvider;
            _payJoinStateProvider = payJoinStateProvider;
        }

        [HttpPost("{invoice}")]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        [MediaTypeConstraint("text/plain")]
        [RateLimitsFilter(ZoneLimits.PayJoin, Scope = RateLimitsScope.RemoteAddress)]
        public async Task<IActionResult> Submit(string cryptoCode, string invoice)
        {
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (network == null)
            {
                return UnprocessableEntity("Incorrect network");
            }

            string rawBody;
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrEmpty(rawBody))
            {
                return UnprocessableEntity("raw tx not provided");
            }

            PSBT psbt = null;
            if (!Transaction.TryParse(rawBody, network.NBitcoinNetwork, out var transaction) &&
                !PSBT.TryParse(rawBody, network.NBitcoinNetwork, out psbt))
            {
                return UnprocessableEntity("invalid raw transaction or psbt");
            }

            if (psbt != null)
            {
                transaction = psbt.ExtractTransaction();
            }

            if (transaction.Check() != TransactionCheckResult.Success)
            {
                return UnprocessableEntity($"invalid tx: {transaction.Check()}");
            }

            if (transaction.Inputs.Any(txin => txin.ScriptSig == null || txin.WitScript == null))
            {
                return UnprocessableEntity($"all inputs must be segwit and signed");
            }

            var explorerClient = _explorerClientProvider.GetExplorerClient(network);
            var mempool = await explorerClient.BroadcastAsync(transaction, true);
            if (!mempool.Success)
            {
                return UnprocessableEntity($"provided transaction isn't mempool eligible {mempool.RPCCodeMessage}");
            }

            var paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike);

            //multiple outs could mean a payment being done to multiple invoices to multiple stores in one payjoin tx which makes life unbearable
            //UNLESS the request specified an invoice Id, which is mandatory :) 
            var matchingInvoice = await _invoiceRepository.GetInvoice(invoice);
            if (matchingInvoice == null)
            {
                return UnprocessableEntity($"invalid invoice");
            }
            
            var invoicePaymentMethod = matchingInvoice.GetPaymentMethod(paymentMethodId);
            //get outs to our current invoice address
            var currentPaymentMethodDetails =
                invoicePaymentMethod.GetPaymentMethodDetails() as BitcoinLikeOnChainPaymentMethod;

            if (!currentPaymentMethodDetails.PayJoin.Enabled)
            {
                return UnprocessableEntity($"cannot handle payjoin tx");
            }

            //the invoice must be active, and the status must be new OR paid if 
            if (matchingInvoice.IsExpired() ||
                ((matchingInvoice.GetInvoiceState().Status == InvoiceStatus.Paid &&
                  currentPaymentMethodDetails.PayJoin.OriginalTransactionHash == null) ||
                 matchingInvoice.GetInvoiceState().Status != InvoiceStatus.New)) 
            {
                return UnprocessableEntity($"cannot handle payjoin tx");
            }


            if (currentPaymentMethodDetails.PayJoin.OriginalTransactionHash != null &&
                currentPaymentMethodDetails.PayJoin.OriginalTransactionHash != transaction.GetHash() && 
                !transaction.RBF)
            {
                return UnprocessableEntity($"cannot handle payjoin tx");
            }

            var address = currentPaymentMethodDetails.GetDepositAddress(network.NBitcoinNetwork);
            var matchingTXOuts = transaction.Outputs.Where(txout => txout.IsTo(address));
            var nonMatchingTXOuts = transaction.Outputs.Where(txout => !txout.IsTo(address));
            if (!matchingTXOuts.Any())
            {
                return UnprocessableEntity($"tx does not pay invoice");
            }

            var store = await _storeRepository.FindStore(matchingInvoice.StoreId);

            //check if store is enabled
            var derivationSchemeSettings = store.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<DerivationSchemeSettings>().SingleOrDefault(settings =>
                    settings.PaymentId == paymentMethodId && store.GetEnabledPaymentIds(_btcPayNetworkProvider)
                        .Contains(settings.PaymentId));
            if (derivationSchemeSettings == null)
            {
                return UnprocessableEntity($"cannot handle payjoin tx");
            }

            var state = _payJoinStateProvider.GetOrAdd(new WalletId(matchingInvoice.StoreId, cryptoCode),
                derivationSchemeSettings.AccountDerivation);

            //check if any of the inputs have been spotted in other txs sent our way..Reject anything but the original  
            //also reject if the invoice being payjoined to already has a record
            var validity = state.CheckIfTransactionValid(transaction, invoice);
            if (validity == PayJoinState.TransactionValidityResult.Invalid_Inputs_Seen || validity == PayJoinState.TransactionValidityResult.Invalid_PartialMatch)
            {
                return UnprocessableEntity($"cannot handle payjoin tx");
            }

            //check if wallet of store is configured to be hot wallet
            var extKeyStr = await explorerClient.GetMetadataAsync<string>(
                derivationSchemeSettings.AccountDerivation,
                WellknownMetadataKeys.MasterHDKey);
            if (extKeyStr == null)
            {
                return UnprocessableEntity($"cannot handle payjoin tx");
            }

            var extKey = ExtKey.Parse(extKeyStr, network.NBitcoinNetwork);

            var signingKeySettings = derivationSchemeSettings.GetSigningAccountKeySettings();
            if (signingKeySettings.RootFingerprint is null)
                signingKeySettings.RootFingerprint = extKey.GetPublicKey().GetHDFingerPrint();

            RootedKeyPath rootedKeyPath = signingKeySettings.GetRootedKeyPath();
            if (rootedKeyPath == null)
            {
                return UnprocessableEntity($"cannot handle payjoin tx");
                // The master fingerprint and/or account key path of your seed are not set in the wallet settings
            }

            // The user gave the root key, let's try to rebase the PSBT, and derive the account private key
            if (rootedKeyPath.MasterFingerprint == extKey.GetPublicKey().GetHDFingerPrint())
            {
                extKey = extKey.Derive(rootedKeyPath.KeyPath);
            }

            //check if the store uses segwit -- mixing inputs of different types is suspicious
            if (derivationSchemeSettings.AccountDerivation.ScriptPubKeyType() == ScriptPubKeyType.Legacy)
            {
                return UnprocessableEntity($"cannot handle payjoin tx");
            }

            //get previous payments so that we can check if their address is also used in the txouts)
            var previousPayments = matchingInvoice.GetPayments(network)
                .Select(entity => entity.GetCryptoPaymentData() as BitcoinLikePaymentData);

            if (transaction.Outputs.Any(
                txout => previousPayments.Any(data => !txout.IsTo(address) && txout.IsTo(data.GetDestination()))))
            {
                //Meh, address reuse from the customer would be happening with this tx, skip 
                return UnprocessableEntity($"cannot handle payjoin tx");
            }

            //get any utxos we exposed already that match any of the inputs sent to us. 
            var utxosToContributeToThisPayment = state.GetExposed(transaction);

            var invoicePaymentMethodAccounting = invoicePaymentMethod.Calculate();
            if (invoicePaymentMethodAccounting.Due != matchingTXOuts.Sum(txout => txout.Value) &&
                !utxosToContributeToThisPayment.Any())
            {
                //the invoice would be under/overpaid with this tx and we have not exposed utxos so no worries
                return UnprocessableEntity($"cannot handle payjoin tx");
            }

            //if we have not exposed any utxos to any of the inputs
            if (!utxosToContributeToThisPayment.Any())
            {
                var wallet = _btcPayWalletProvider.GetWallet(network);
                //get all utxos we have so far exposed
                var coins = state.GetRecords().SelectMany(list =>
                    list.CoinsExposed.Select(coin => coin.OutPoint.Hash));

                //get all utxos we have NOT so far exposed
                var availableUtxos = (await wallet.GetUnspentCoins(derivationSchemeSettings.AccountDerivation)).Where(
                    coin =>
                        !coins.Contains(coin.OutPoint.Hash));
                if (availableUtxos.Any())
                {
                    //clean up the state by removing utxos from the exposed list that we no longer have
                    state.PruneExposedButSpentCoins(availableUtxos);
                    //if we have coins that were exposed before but were not spent, prioritize them
                    var exposedAlready = state.GetExposedCoins();
                    if (exposedAlready.Any())
                    {
                        utxosToContributeToThisPayment = SelectCoins(network, exposedAlready,
                            invoicePaymentMethodAccounting.Due.ToDecimal(MoneyUnit.BTC),
                            nonMatchingTXOuts.Select(txout => txout.Value.ToDecimal(MoneyUnit.BTC)));
                        state.PruneExposedBySpentCoins(utxosToContributeToThisPayment.Select(coin => coin.OutPoint));
                    }
                    else
                    {
                        utxosToContributeToThisPayment = SelectCoins(network, availableUtxos,
                            invoicePaymentMethodAccounting.Due.ToDecimal(MoneyUnit.BTC),
                            nonMatchingTXOuts.Select(txout => txout.Value.ToDecimal(MoneyUnit.BTC)));
                    }
                }
            }

            //we don't have any utxos to provide to this tx
            if (!utxosToContributeToThisPayment.Any())
            {
                return UnprocessableEntity($"cannot handle payjoin tx");
            }

            //we rebuild the tx using 1 output to the invoice designed address
            var cjOutputContributedAmount = utxosToContributeToThisPayment.Sum(coin => coin.Value.GetValue(network));
            var cjOutputSum = matchingTXOuts.Sum(txout => txout.Value.ToDecimal(MoneyUnit.BTC)) +
                              cjOutputContributedAmount;

            var newTx = transaction.Clone();


            if (matchingTXOuts.Count() > 1)
            {
                //if there are more than 1 outputs to our address, consolidate them to 1 + coinjoined amount to avoid unnecessary utxos
                newTx.Outputs.Clear();
                newTx.Outputs.Add(new Money(cjOutputSum, MoneyUnit.BTC), address.ScriptPubKey);
                foreach (var nonmatchingTxOut in nonMatchingTXOuts)
                {
                    newTx.Outputs.Add(nonmatchingTxOut.Value, nonmatchingTxOut.ScriptPubKey);
                }
            }
            else
            {
                //set the value of the out to our address to the sum of the coinjoined amount
                foreach (var txOutput in newTx.Outputs.Where(txOutput =>
                    txOutput.Value == matchingTXOuts.First().Value &&
                    txOutput.ScriptPubKey == matchingTXOuts.First().ScriptPubKey))
                {
                    txOutput.Value = new Money(cjOutputSum, MoneyUnit.BTC);
                    break;
                }
            }

            newTx.Inputs.AddRange(utxosToContributeToThisPayment.Select(coin =>
                new TxIn(coin.OutPoint) {Sequence = newTx.Inputs.First().Sequence}));

            if (psbt != null)
            {
                psbt = PSBT.FromTransaction(newTx, network.NBitcoinNetwork);

                psbt = (await explorerClient.UpdatePSBTAsync(new UpdatePSBTRequest()
                {
                    DerivationScheme = derivationSchemeSettings.AccountDerivation,
                    PSBT = psbt,
                    RebaseKeyPaths = derivationSchemeSettings.GetPSBTRebaseKeyRules().ToList()
                })).PSBT;


                psbt = psbt.SignWithKeys(utxosToContributeToThisPayment
                    .Select(coin => extKey.Derive(coin.KeyPath).PrivateKey)
                    .ToArray());

                if (validity == PayJoinState.TransactionValidityResult.Valid_SameInputs)
                {
                    //if the invoice was rbfed, remove the current record and replace it with the new one
                    state.RemoveRecord(invoice);
                }
                if (validity == PayJoinState.TransactionValidityResult.Valid_NoMatch)
                {
                    await AddRecord(invoice, state, transaction, utxosToContributeToThisPayment,
                        cjOutputContributedAmount, cjOutputSum, newTx, currentPaymentMethodDetails,
                        invoicePaymentMethod);
                }

                return Ok(HexEncoder.IsWellFormed(rawBody) ? psbt.ToHex() : psbt.ToBase64());
            }
            else
            {
                // Since we're going to modify the transaction, we're going invalidate all signatures
                foreach (TxIn newTxInput in newTx.Inputs)
                {
                    newTxInput.WitScript = WitScript.Empty;
                }

                newTx.Sign(
                    utxosToContributeToThisPayment.Select(coin =>
                        extKey.Derive(coin.KeyPath).PrivateKey.GetWif(network.NBitcoinNetwork)),
                    utxosToContributeToThisPayment.Select(coin => coin.Coin));

                if (validity == PayJoinState.TransactionValidityResult.Valid_SameInputs)
                {
                    //if the invoice was rbfed, remove the current record and replace it with the new one
                    state.RemoveRecord(invoice);
                }
                if (validity == PayJoinState.TransactionValidityResult.Valid_NoMatch)
                {
                    await AddRecord(invoice, state, transaction, utxosToContributeToThisPayment,
                        cjOutputContributedAmount, cjOutputSum, newTx, currentPaymentMethodDetails,
                        invoicePaymentMethod);
                }

                return Ok(newTx.ToHex());
            }
        }

        private async Task AddRecord(string invoice, PayJoinState joinState, Transaction transaction,
            List<ReceivedCoin> utxosToContributeToThisPayment, decimal cjOutputContributedAmount, decimal cjOutputSum,
            Transaction newTx,
            BitcoinLikeOnChainPaymentMethod currentPaymentMethodDetails, PaymentMethod invoicePaymentMethod)
        {
            //keep a record of the tx and check if we have seen the tx before or any of its inputs
            //on a timer service: if x amount of times passes, broadcast this tx
            joinState.AddRecord(new PayJoinStateRecordedItem()
            {
                Timestamp = DateTimeOffset.Now,
                Transaction = transaction,
                OriginalTransactionHash = transaction.GetHash(),
                CoinsExposed = utxosToContributeToThisPayment,
                ContributedAmount = cjOutputContributedAmount,
                TotalOutputAmount = cjOutputSum,
                ProposedTransactionHash = newTx.GetHash(),
                InvoiceId = invoice
            });
            //we also store a record in the payment method details of the invoice,
            //Tn case the server is shut down and a payjoin payment is made before it is turned back on. 
            //Otherwise we would end up marking the invoice as overPaid with our own inputs!
            currentPaymentMethodDetails.PayJoin = new PayJoinPaymentState()
            {
                Enabled = true,
                CoinsExposed = utxosToContributeToThisPayment,
                ContributedAmount = cjOutputContributedAmount,
                TotalOutputAmount = cjOutputSum,
                ProposedTransactionHash = newTx.GetHash(),
                OriginalTransactionHash = transaction.GetHash(),
            };
            invoicePaymentMethod.SetPaymentMethodDetails(currentPaymentMethodDetails);
            await _invoiceRepository.UpdateInvoicePaymentMethod(invoice, invoicePaymentMethod);
        }

        private List<ReceivedCoin> SelectCoins(BTCPayNetwork network, IEnumerable<ReceivedCoin> availableUtxos,
            decimal paymentAmount, IEnumerable<decimal> otherOutputs)
        {
            // UIH = "unnecessary input heuristic", basically "a wallet wouldn't choose more utxos to spend in this scenario".
            //
            // "UIH1" : one output is smaller than any input. This heuristically implies that that output is not a payment, and must therefore be a change output.
            //
            // "UIH2": one input is larger than any output. This heuristically implies that no output is a payment, or, to say it better, it implies that this is not a normal wallet-created payment, it's something strange/exotic.
            //src: https://gist.github.com/AdamISZ/4551b947789d3216bacfcb7af25e029e#gistcomment-2796539

            foreach (var availableUtxo in availableUtxos)
            {
                //we can only check against our input as we dont know the value of the rest.
                var input = availableUtxo.Value.GetValue(network);
                var paymentAmountSum = input + paymentAmount;
                if (otherOutputs.Concat(new[] {paymentAmountSum}).Any(output => input > output))
                {
                    //UIH 1 & 2
                    continue;
                }

                return new List<ReceivedCoin> {availableUtxo};
            }

            //For now we just grab a utxo "at random"
            Random r = new Random();
            return new List<ReceivedCoin>() {availableUtxos.ElementAt(r.Next(0, availableUtxos.Count()))};
        }
    }
}
