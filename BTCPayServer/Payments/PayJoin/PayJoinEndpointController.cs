using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Filters;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Logging;
using NBXplorer;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;
using NicolasDorier.RateLimits;
using Microsoft.Extensions.Logging;

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
        private readonly PayJoinRepository _payJoinRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly NBXplorerDashboard _dashboard;
        private readonly DelayedTransactionBroadcaster _broadcaster;

        public PayJoinEndpointController(BTCPayNetworkProvider btcPayNetworkProvider,
            InvoiceRepository invoiceRepository, ExplorerClientProvider explorerClientProvider,
            StoreRepository storeRepository, BTCPayWalletProvider btcPayWalletProvider,
            PayJoinRepository payJoinRepository,
            EventAggregator eventAggregator,
            NBXplorerDashboard dashboard,
            DelayedTransactionBroadcaster broadcaster)
        {
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _invoiceRepository = invoiceRepository;
            _explorerClientProvider = explorerClientProvider;
            _storeRepository = storeRepository;
            _btcPayWalletProvider = btcPayWalletProvider;
            _payJoinRepository = payJoinRepository;
            _eventAggregator = eventAggregator;
            _dashboard = dashboard;
            _broadcaster = broadcaster;
        }

        [HttpPost("")]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        [MediaTypeConstraint("text/plain")]
        [RateLimitsFilter(ZoneLimits.PayJoin, Scope = RateLimitsScope.RemoteAddress)]
        public async Task<IActionResult> Submit(string cryptoCode)
        {
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (network == null)
            {
                return BadRequest(CreatePayjoinError(400, "invalid-network", "Incorrect network"));
            }

            var explorer = _explorerClientProvider.GetExplorerClient(network);
            if (Request.ContentLength is long length)
            {
                if (length > 1_000_000)
                    return this.StatusCode(413,
                        CreatePayjoinError(413, "payload-too-large", "The transaction is too big to be processed"));
            }
            else
            {
                return StatusCode(411,
                    CreatePayjoinError(411, "missing-content-length",
                        "The http header Content-Length should be filled"));
            }

            string rawBody;
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                rawBody = (await reader.ReadToEndAsync()) ?? string.Empty;
            }

            Transaction originalTx = null;
            FeeRate originalFeeRate = null;
            bool psbtFormat = true;
            if (!PSBT.TryParse(rawBody, network.NBitcoinNetwork, out var psbt))
            {
                psbtFormat = false;
                if (!Transaction.TryParse(rawBody, network.NBitcoinNetwork, out var tx))
                    return BadRequest(CreatePayjoinError(400, "invalid-format", "invalid transaction or psbt"));
                originalTx = tx;
                psbt = PSBT.FromTransaction(tx, network.NBitcoinNetwork);
                psbt = (await explorer.UpdatePSBTAsync(new UpdatePSBTRequest() {PSBT = psbt})).PSBT;
                for (int i = 0; i < tx.Inputs.Count; i++)
                {
                    psbt.Inputs[i].FinalScriptSig = tx.Inputs[i].ScriptSig;
                    psbt.Inputs[i].FinalScriptWitness = tx.Inputs[i].WitScript;
                }
            }
            else
            {
                if (!psbt.IsAllFinalized())
                    return BadRequest(CreatePayjoinError(400, "psbt-not-finalized", "The PSBT should be finalized"));
                originalTx = psbt.ExtractTransaction();
            }
            
            if (originalTx.Inputs.Any(i => !(i.GetSigner() is WitKeyId)))
                return BadRequest(CreatePayjoinError(400, "not-using-p2wpkh", "Payjoin only support P2WPKH inputs"));
            if (psbt.CheckSanity() is var errors && errors.Count != 0)
            {
                return BadRequest(CreatePayjoinError(400, "insane-psbt", $"This PSBT is insane ({errors[0]})"));
            }
            if (!psbt.TryGetEstimatedFeeRate(out originalFeeRate))
            {
                return BadRequest(CreatePayjoinError(400, "need-utxo-information",
                    "You need to provide Witness UTXO information to the PSBT."));
            }

            // This is actually not a mandatory check, but we don't want implementers
            // to leak global xpubs
            if (psbt.GlobalXPubs.Any())
            {
                return BadRequest(CreatePayjoinError(400, "leaking-data",
                    "GlobalXPubs should not be included in the PSBT"));
            }

            if (psbt.Outputs.Any(o => o.HDKeyPaths.Count != 0) || psbt.Inputs.Any(o => o.HDKeyPaths.Count != 0))
            {
                return BadRequest(CreatePayjoinError(400, "leaking-data",
                    "Keypath information should not be included in the PSBT"));
            }

            if (psbt.Inputs.Any(o => !o.IsFinalized()))
            {
                return BadRequest(CreatePayjoinError(400, "psbt-not-finalized", "The PSBT Should be finalized"));
            }
            ////////////

            var mempool = await explorer.BroadcastAsync(originalTx, true);
            if (!mempool.Success)
            {
                return BadRequest(CreatePayjoinError(400, "invalid-transaction",
                    $"Provided transaction isn't mempool eligible {mempool.RPCCodeMessage}"));
            }

            var paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike);
            bool paidSomething = false;
            Money due = null;
            Dictionary<OutPoint, UTXO> selectedUTXOs = new Dictionary<OutPoint, UTXO>();
            PSBTOutput paymentOutput = null;
            BitcoinAddress paymentAddress = null;
            InvoiceEntity invoice = null;
            int ourOutputIndex = -1;
            DerivationSchemeSettings derivationSchemeSettings = null;
            foreach (var output in psbt.Outputs)
            {
                ourOutputIndex++;
                var key = output.ScriptPubKey.Hash + "#" + network.CryptoCode.ToUpperInvariant();
                invoice = (await _invoiceRepository.GetInvoicesFromAddresses(new[] {key})).FirstOrDefault();
                if (invoice is null)
                    continue;
                derivationSchemeSettings = invoice.GetSupportedPaymentMethod<DerivationSchemeSettings>(paymentMethodId)
                    .SingleOrDefault();
                if (derivationSchemeSettings is null)
                    continue;
                var paymentMethod = invoice.GetPaymentMethod(paymentMethodId);
                var paymentDetails =
                    paymentMethod.GetPaymentMethodDetails() as Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod;
                if (paymentDetails is null || !paymentDetails.PayjoinEnabled)
                    continue;
                if (invoice.GetAllBitcoinPaymentData().Any())
                {
                    return UnprocessableEntity(CreatePayjoinError(422, "already-paid",
                        $"The invoice this PSBT is paying has already been partially or completely paid"));
                }

                paidSomething = true;
                due = paymentMethod.Calculate().TotalDue - output.Value;
                if (due > Money.Zero)
                {
                    break;
                }

                if (!await _payJoinRepository.TryLockInputs(originalTx.Inputs.Select(i => i.PrevOut).ToArray()))
                {
                    return BadRequest(CreatePayjoinError(400, "inputs-already-used",
                        "Some of those inputs have already been used to make payjoin transaction"));
                }

                var utxos = (await explorer.GetUTXOsAsync(derivationSchemeSettings.AccountDerivation))
                    .GetUnspentUTXOs(false);
                // In case we are paying ourselves, be need to make sure
                // we can't take spent outpoints.
                var prevOuts = originalTx.Inputs.Select(o => o.PrevOut).ToHashSet();
                utxos = utxos.Where(u => !prevOuts.Contains(u.Outpoint)).ToArray();
                foreach (var utxo in await SelectUTXO(network, utxos, output.Value,
                    psbt.Outputs.Where(o => o.Index != output.Index).Select(o => o.Value).ToArray()))
                {
                    selectedUTXOs.Add(utxo.Outpoint, utxo);
                }

                paymentOutput = output;
                paymentAddress = paymentDetails.GetDepositAddress(network.NBitcoinNetwork);
                break;
            }

            if (!paidSomething)
            {
                return BadRequest(CreatePayjoinError(400, "invoice-not-found",
                    "This transaction does not pay any invoice with payjoin"));
            }

            if (due is null || due > Money.Zero)
            {
                return BadRequest(CreatePayjoinError(400, "invoice-not-fully-paid",
                    "The transaction must pay the whole invoice"));
            }

            if (selectedUTXOs.Count == 0)
            {
                await _explorerClientProvider.GetExplorerClient(network).BroadcastAsync(originalTx);
                return StatusCode(503,
                    CreatePayjoinError(503, "out-of-utxos",
                        "We do not have any UTXO available for making a payjoin for now"));
            }

            var originalPaymentValue = paymentOutput.Value;
            // Add the original transaction to the payment
            var originalPaymentData = new BitcoinLikePaymentData(paymentAddress,
                paymentOutput.Value,
                new OutPoint(originalTx.GetHash(), paymentOutput.Index),
                originalTx.RBF);
            originalPaymentData.PayjoinInformation = new PayjoinInformation()
            {
                Type = PayjoinTransactionType.Original, ContributedOutPoints = selectedUTXOs.Select(o => o.Key).ToArray()
            };
            originalPaymentData.ConfirmationCount = -1;
            var now = DateTimeOffset.UtcNow;
            var payment = await _invoiceRepository.AddPayment(invoice.Id, now, originalPaymentData, network, true);
            if (payment is null)
            {
                return UnprocessableEntity(CreatePayjoinError(422, "already-paid",
                    $"The original transaction has already been accounted"));
            }

            await _broadcaster.Schedule(now + TimeSpan.FromMinutes(1.0), originalTx, network);
            await _btcPayWalletProvider.GetWallet(network).SaveOffchainTransactionAsync(originalTx);
            _eventAggregator.Publish(new InvoiceEvent(invoice, 1002, InvoiceEvent.ReceivedPayment) {Payment = payment});

            //check if wallet of store is configured to be hot wallet
            var extKeyStr = await explorer.GetMetadataAsync<string>(
                derivationSchemeSettings.AccountDerivation,
                WellknownMetadataKeys.AccountHDKey);
            if (extKeyStr == null)
            {
                // This should not happen, as we check the existance of private key before creating invoice with payjoin
                return StatusCode(500, CreatePayjoinError(500, "unavailable", $"This service is unavailable for now"));
            }

            var newTx = originalTx.Clone();
            var ourOutput = newTx.Outputs[ourOutputIndex];
            foreach (var selectedUTXO in selectedUTXOs.Select(o => o.Value))
            {
                ourOutput.Value += (Money)selectedUTXO.Value;
                newTx.Inputs.Add(selectedUTXO.Outpoint);
            }

            var rand = new Random();
            Utils.Shuffle(newTx.Inputs, rand);
            Utils.Shuffle(newTx.Outputs, rand);
            ourOutputIndex = newTx.Outputs.IndexOf(ourOutput);

            // Remove old signatures as they are not valid anymore
            foreach (var input in newTx.Inputs)
            {
                input.WitScript = WitScript.Empty;
            }

            Money ourFeeContribution = Money.Zero;
            // We need to adjust the fee to keep a constant fee rate
            var originalNewTx = newTx.Clone();
            bool isSecondPass = false;
            recalculateFee:
            ourOutput = newTx.Outputs[ourOutputIndex];
            var txBuilder = network.NBitcoinNetwork.CreateTransactionBuilder();
            txBuilder.AddCoins(psbt.Inputs.Select(i => i.GetCoin()));
            txBuilder.AddCoins(selectedUTXOs.Select(o => o.Value.AsCoin()));
            Money expectedFee = txBuilder.EstimateFees(newTx, originalFeeRate);
            Money actualFee = newTx.GetFee(txBuilder.FindSpentCoins(newTx));
            Money additionalFee = expectedFee - actualFee;
            if (additionalFee > Money.Zero)
            {
                var minRelayTxFee = this._dashboard.Get(network.CryptoCode).Status.BitcoinStatus?.MinRelayTxFee ??
                                    new FeeRate(1.0m);

                // If the user overpaid, taking fee on our output (useful if they dump a full UTXO for privacy)
                if (due < Money.Zero)
                {
                    ourFeeContribution = Money.Min(additionalFee, -due);
                    ourFeeContribution = Money.Min(ourFeeContribution,
                        ourOutput.Value - ourOutput.GetDustThreshold(minRelayTxFee));
                    ourOutput.Value -= ourFeeContribution;
                    additionalFee -= ourFeeContribution;
                }

                // The rest, we take from user's change
                if (additionalFee > Money.Zero)
                {
                    for (int i = 0; i < newTx.Outputs.Count && additionalFee != Money.Zero; i++)
                    {
                        if (i != ourOutputIndex)
                        {
                            var outputContribution = Money.Min(additionalFee, newTx.Outputs[i].Value);
                            newTx.Outputs[i].Value -= outputContribution;
                            additionalFee -= outputContribution;
                        }
                    }
                }

                List<int> dustIndices = new List<int>();
                for (int i = 0; i < newTx.Outputs.Count; i++)
                {
                    if (newTx.Outputs[i].IsDust(minRelayTxFee))
                    {
                        dustIndices.Insert(0, i);
                    }
                }

                if (dustIndices.Count > 0)
                {
                    if (isSecondPass)
                    {
                        // This should not happen
                        return StatusCode(500,
                            CreatePayjoinError(500, "unavailable",
                                $"This service is unavailable for now (isSecondPass)"));
                    }

                    foreach (var dustIndex in dustIndices)
                    {
                        newTx.Outputs.RemoveAt(dustIndex);
                    }

                    ourOutputIndex = newTx.Outputs.IndexOf(ourOutput);
                    newTx = originalNewTx.Clone();
                    foreach (var dustIndex in dustIndices)
                    {
                        newTx.Outputs.RemoveAt(dustIndex);
                    }
                    ourFeeContribution = Money.Zero;
                    isSecondPass = true;
                    goto recalculateFee;
                }

                if (additionalFee > Money.Zero)
                {
                    // We could not pay fully the additional fee, however, as long as
                    // we are not under the relay fee, it should be OK.
                    var newVSize = txBuilder.EstimateSize(newTx, true);
                    var newFeePaid = newTx.GetFee(txBuilder.FindSpentCoins(newTx));
                    if (new FeeRate(newFeePaid, newVSize) < minRelayTxFee)
                    {
                        await _payJoinRepository.TryUnlock(selectedUTXOs.Select(o => o.Key).ToArray());
                        return UnprocessableEntity(CreatePayjoinError(422, "not-enough-money",
                            "Not enough money is sent to pay for the additional payjoin inputs"));
                    }
                }
            }

            var accountKey = ExtKey.Parse(extKeyStr, network.NBitcoinNetwork);
            var newPsbt = PSBT.FromTransaction(newTx, network.NBitcoinNetwork);
            foreach (var selectedUtxo in selectedUTXOs.Select(o => o.Value))
            {
                var signedInput = newPsbt.Inputs.FindIndexedInput(selectedUtxo.Outpoint);
                signedInput.UpdateFromCoin(selectedUtxo.AsCoin());
                var privateKey = accountKey.Derive(selectedUtxo.KeyPath).PrivateKey;
                signedInput.Sign(privateKey);
                signedInput.FinalizeInput();
                newTx.Inputs[signedInput.Index].WitScript = newPsbt.Inputs[(int)signedInput.Index].FinalScriptWitness;
            }

            // Add the coinjoin transaction to the payments
            var coinjoinPaymentData = new BitcoinLikePaymentData(paymentAddress,
                originalPaymentValue - ourFeeContribution,
                new OutPoint(newPsbt.GetGlobalTransaction().GetHash(), ourOutputIndex),
                originalTx.RBF);
            coinjoinPaymentData.PayjoinInformation = new PayjoinInformation()
            {
                Type = PayjoinTransactionType.Coinjoin,
                ContributedOutPoints = selectedUTXOs.Select(o => o.Key).ToArray()
            };
            coinjoinPaymentData.ConfirmationCount = -1;
            payment = await _invoiceRepository.AddPayment(invoice.Id, now, coinjoinPaymentData, network, false,
                payment.NetworkFee);
            // We do not publish an event on purpose, this would be confusing for the merchant.

            if (psbtFormat)
                return Ok(newPsbt.ToBase64());
            else
                return Ok(newTx.ToHex());
        }

        private JObject CreatePayjoinError(int httpCode, string errorCode, string friendlyMessage)
        {
            var o = new JObject();
            o.Add(new JProperty("httpCode", httpCode));
            o.Add(new JProperty("errorCode", errorCode));
            o.Add(new JProperty("message", friendlyMessage));
            return o;
        }

        private async Task<UTXO[]> SelectUTXO(BTCPayNetwork network, UTXO[] availableUtxos, Money paymentAmount,
            Money[] otherOutputs)
        {
            if (availableUtxos.Length == 0)
                return Array.Empty<UTXO>();
            // Assume the merchant wants to get rid of the dust
            Utils.Shuffle(availableUtxos);
            HashSet<OutPoint> locked = new HashSet<OutPoint>();   
            // We don't want to make too many db roundtrip which would be inconvenient for the sender
            int maxTries = 30;
            int currentTry = 0;
            List<UTXO> utxosByPriority = new List<UTXO>();
            // UIH = "unnecessary input heuristic", basically "a wallet wouldn't choose more utxos to spend in this scenario".
            //
            // "UIH1" : one output is smaller than any input. This heuristically implies that that output is not a payment, and must therefore be a change output.
            //
            // "UIH2": one input is larger than any output. This heuristically implies that no output is a payment, or, to say it better, it implies that this is not a normal wallet-created payment, it's something strange/exotic.
            //src: https://gist.github.com/AdamISZ/4551b947789d3216bacfcb7af25e029e#gistcomment-2796539

            foreach (var availableUtxo in availableUtxos)
            {
                if (currentTry >= maxTries)
                    break;
                //we can only check against our input as we dont know the value of the rest.
                var input = (Money)availableUtxo.Value;
                var paymentAmountSum = input + paymentAmount;
                if (otherOutputs.Concat(new[] {paymentAmountSum}).Any(output => input > output))
                {
                    //UIH 1 & 2
                    continue;
                }

                if (await _payJoinRepository.TryLock(availableUtxo.Outpoint))
                {
                    return new UTXO[] { availableUtxo };
                }
                locked.Add(availableUtxo.Outpoint);
                currentTry++;
            }
            foreach (var utxo in availableUtxos.Where(u => !locked.Contains(u.Outpoint)))
            {
                if (currentTry >= maxTries)
                    break;
                if (await _payJoinRepository.TryLock(utxo.Outpoint))
                {
                    return new UTXO[] { utxo };
                }
                currentTry++;
            }
            return Array.Empty<UTXO>();
        }
    }
}
