using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.Filters;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;
using NicolasDorier.RateLimits;
using NBXplorer.DerivationStrategy;
using System.Diagnostics.CodeAnalysis;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Payments.PayJoin
{
    [Route("{cryptoCode}/" + PayjoinClient.BIP21EndpointKey)]
    public class PayJoinEndpointController : ControllerBase
    {
        /// <summary>
        /// This comparer sorts utxo in a deterministic manner
        /// based on a random parameter.
        /// When a UTXO is locked because used in a coinjoin, in might be unlocked
        /// later if the coinjoin failed.
        /// Such UTXO should be reselected in priority so we don't expose the other UTXOs.
        /// By making sure this UTXO is almost always coming on the same order as before it was locked,
        /// it will more likely be selected again.
        /// </summary>
        internal class UTXODeterministicComparer : IComparer<UTXO>
        {
            static UTXODeterministicComparer()
            {
                _Instance = new UTXODeterministicComparer(RandomUtils.GetUInt256());
            }

            public UTXODeterministicComparer(uint256 blind)
            {
                _blind = blind.ToBytes();
            }

            static UTXODeterministicComparer _Instance;
            private byte[] _blind;

            public static UTXODeterministicComparer Instance => _Instance;
            public int Compare([AllowNull] UTXO x, [AllowNull] UTXO y)
            {
                if (x == null)
                    throw new ArgumentNullException(nameof(x));
                if (y == null)
                    throw new ArgumentNullException(nameof(y));
                Span<byte> tmpx = stackalloc byte[32];
                Span<byte> tmpy = stackalloc byte[32];
                x.Outpoint.Hash.ToBytes(tmpx);
                y.Outpoint.Hash.ToBytes(tmpy);
                for (int i = 0; i < 32; i++)
                {
                    if ((byte)(tmpx[i] ^ _blind[i]) < (byte)(tmpy[i] ^ _blind[i]))
                    {
                        return 1;
                    }
                    if ((byte)(tmpx[i] ^ _blind[i]) > (byte)(tmpy[i] ^ _blind[i]))
                    {
                        return -1;
                    }
                }
                return x.Outpoint.N.CompareTo(y.Outpoint.N);
            }
        }
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

            async Task BroadcastNow()
            {
                await _explorerClientProvider.GetExplorerClient(network).BroadcastAsync(originalTx);
            }

            var sendersInputType = psbt.GetInputsScriptPubKeyType();
            if (sendersInputType is null)
                return BadRequest(CreatePayjoinError(400, "unsupported-inputs", "Payjoin only support segwit inputs (of the same type)"));
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

            async Task UnlockUTXOs()
            {
                await _payJoinRepository.TryUnlock(selectedUTXOs.Select(o => o.Key).ToArray());
            }
            PSBTOutput originalPaymentOutput = null;
            BitcoinAddress paymentAddress = null;
            InvoiceEntity invoice = null;
            DerivationSchemeSettings derivationSchemeSettings = null;
            foreach (var output in psbt.Outputs)
            {
                var key = output.ScriptPubKey.Hash + "#" + network.CryptoCode.ToUpperInvariant();
                invoice = (await _invoiceRepository.GetInvoicesFromAddresses(new[] {key})).FirstOrDefault();
                if (invoice is null)
                    continue;
                derivationSchemeSettings = invoice.GetSupportedPaymentMethod<DerivationSchemeSettings>(paymentMethodId)
                    .SingleOrDefault();
                if (derivationSchemeSettings is null)
                    continue;

                var receiverInputsType = derivationSchemeSettings.AccountDerivation.ScriptPubKeyType();
                if (!PayjoinClient.SupportedFormats.Contains(receiverInputsType))
                {
                    //this should never happen, unless the store owner changed the wallet mid way through an invoice
                    return StatusCode(500, CreatePayjoinError(500, "unavailable", $"This service is unavailable for now"));
                }
                if (sendersInputType != receiverInputsType)
                {
                    return StatusCode(503,
                        CreatePayjoinError(503, "out-of-utxos",
                            "We do not have any UTXO available for making a payjoin with the sender's inputs type")); 
                }
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
                Array.Sort(utxos, UTXODeterministicComparer.Instance);
                foreach (var utxo in await SelectUTXO(network, utxos, output.Value,
                    psbt.Outputs.Where(o => o.Index != output.Index).Select(o => o.Value).ToArray()))
                {
                    selectedUTXOs.Add(utxo.Outpoint, utxo);
                }

                originalPaymentOutput = output;
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
                await BroadcastNow();
                return StatusCode(503,
                    CreatePayjoinError(503, "out-of-utxos",
                        "We do not have any UTXO available for making a payjoin for now"));
            }

            var originalPaymentValue = originalPaymentOutput.Value;
            await _broadcaster.Schedule(DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1.0), originalTx, network);

            //check if wallet of store is configured to be hot wallet
            var extKeyStr = await explorer.GetMetadataAsync<string>(
                derivationSchemeSettings.AccountDerivation,
                WellknownMetadataKeys.AccountHDKey);
            if (extKeyStr == null)
            {
                // This should not happen, as we check the existance of private key before creating invoice with payjoin
                await UnlockUTXOs();
                await BroadcastNow();
                return StatusCode(500, CreatePayjoinError(500, "unavailable", $"This service is unavailable for now"));
            }
            
            Money contributedAmount = Money.Zero;
            var newTx = originalTx.Clone();
            var ourNewOutput = newTx.Outputs[originalPaymentOutput.Index];
            HashSet<TxOut> isOurOutput = new HashSet<TxOut>();
            isOurOutput.Add(ourNewOutput);
            foreach (var selectedUTXO in selectedUTXOs.Select(o => o.Value))
            {
                contributedAmount += (Money)selectedUTXO.Value;
                newTx.Inputs.Add(selectedUTXO.Outpoint);
            }
            ourNewOutput.Value += contributedAmount;
            var minRelayTxFee = this._dashboard.Get(network.CryptoCode).Status.BitcoinStatus?.MinRelayTxFee ??
                                new FeeRate(1.0m);

            // Probably receiving some spare change, let's add an output to make
            // it looks more like a normal transaction
            if (newTx.Outputs.Count == 1)
            {
                var change = await explorer.GetUnusedAsync(derivationSchemeSettings.AccountDerivation, DerivationFeature.Change);
                var randomChangeAmount = RandomUtils.GetUInt64() % (ulong)contributedAmount.Satoshi;

                // Randomly round the amount to make the payment output look like a change output
                var roundMultiple = (ulong)Math.Pow(10, (ulong)Math.Log10(randomChangeAmount));
                while (roundMultiple > 1_000UL)
                {
                    if (RandomUtils.GetUInt32() % 2 == 0)
                    {
                        roundMultiple = roundMultiple / 10;
                    }
                    else
                    {
                        randomChangeAmount = (randomChangeAmount / roundMultiple) * roundMultiple;
                        break;
                    }
                }

                var fakeChange = newTx.Outputs.CreateNewTxOut(randomChangeAmount, change.ScriptPubKey);
                if (fakeChange.IsDust(minRelayTxFee))
                {
                    randomChangeAmount = fakeChange.GetDustThreshold(minRelayTxFee);
                    fakeChange.Value = randomChangeAmount;
                }
                if (randomChangeAmount < contributedAmount)
                {
                    ourNewOutput.Value -= fakeChange.Value;
                    newTx.Outputs.Add(fakeChange);
                    isOurOutput.Add(fakeChange);
                }
            }

            var rand = new Random();
            Utils.Shuffle(newTx.Inputs, rand);
            Utils.Shuffle(newTx.Outputs, rand);

            // Remove old signatures as they are not valid anymore
            foreach (var input in newTx.Inputs)
            {
                input.WitScript = WitScript.Empty;
            }

            Money ourFeeContribution = Money.Zero;
            // We need to adjust the fee to keep a constant fee rate
            var txBuilder = network.NBitcoinNetwork.CreateTransactionBuilder();
            txBuilder.AddCoins(psbt.Inputs.Select(i => i.GetSignableCoin()));
            txBuilder.AddCoins(selectedUTXOs.Select(o => o.Value.AsCoin(derivationSchemeSettings.AccountDerivation)));
            Money expectedFee = txBuilder.EstimateFees(newTx, originalFeeRate);
            Money actualFee = newTx.GetFee(txBuilder.FindSpentCoins(newTx));
            Money additionalFee = expectedFee - actualFee;
            if (additionalFee > Money.Zero)
            {
                // If the user overpaid, taking fee on our output (useful if sender dump a full UTXO for privacy)
                for (int i = 0; i < newTx.Outputs.Count && additionalFee > Money.Zero && due < Money.Zero; i++)
                {
                    if (isOurOutput.Contains(newTx.Outputs[i]))
                    {
                        var outputContribution = Money.Min(additionalFee, -due);
                        outputContribution = Money.Min(outputContribution,
                            newTx.Outputs[i].Value - newTx.Outputs[i].GetDustThreshold(minRelayTxFee));
                        newTx.Outputs[i].Value -= outputContribution;
                        additionalFee -= outputContribution;
                        due += outputContribution;
                        ourFeeContribution += outputContribution;
                    }
                }

                // The rest, we take from user's change
                for (int i = 0; i < newTx.Outputs.Count && additionalFee > Money.Zero; i++)
                {
                    if (!isOurOutput.Contains(newTx.Outputs[i]))
                    {
                        var outputContribution = Money.Min(additionalFee, newTx.Outputs[i].Value);
                        outputContribution = Money.Min(outputContribution,
                            newTx.Outputs[i].Value - newTx.Outputs[i].GetDustThreshold(minRelayTxFee));
                        newTx.Outputs[i].Value -= outputContribution;
                        additionalFee -= outputContribution;
                    }
                }

                if (additionalFee > Money.Zero)
                {
                    // We could not pay fully the additional fee, however, as long as
                    // we are not under the relay fee, it should be OK.
                    var newVSize = txBuilder.EstimateSize(newTx, true);
                    var newFeePaid = newTx.GetFee(txBuilder.FindSpentCoins(newTx));
                    if (new FeeRate(newFeePaid, newVSize) < minRelayTxFee)
                    {
                        await UnlockUTXOs();
                        await BroadcastNow();
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
                var coin = selectedUtxo.AsCoin(derivationSchemeSettings.AccountDerivation);
                signedInput.UpdateFromCoin(coin);
                var privateKey = accountKey.Derive(selectedUtxo.KeyPath).PrivateKey;
                signedInput.Sign(privateKey);
                signedInput.FinalizeInput();
                newTx.Inputs[signedInput.Index].WitScript = newPsbt.Inputs[(int)signedInput.Index].FinalScriptWitness;
            }

            // Add the transaction to the payments with a confirmation of -1.
            // This will make the invoice paid even if the user do not
            // broadcast the payjoin.
            var originalPaymentData = new BitcoinLikePaymentData(paymentAddress,
                originalPaymentOutput.Value,
                new OutPoint(originalTx.GetHash(), originalPaymentOutput.Index),
                originalTx.RBF);
            originalPaymentData.ConfirmationCount = -1;
            originalPaymentData.PayjoinInformation = new PayjoinInformation()
            {
                CoinjoinTransactionHash = newPsbt.GetGlobalTransaction().GetHash(),
                CoinjoinValue = originalPaymentValue - ourFeeContribution,
                ContributedOutPoints = selectedUTXOs.Select(o => o.Key).ToArray()
            };
            var payment = await _invoiceRepository.AddPayment(invoice.Id, DateTimeOffset.UtcNow, originalPaymentData, network, true);
            if (payment is null)
            {
                await UnlockUTXOs();
                await BroadcastNow();
                return UnprocessableEntity(CreatePayjoinError(422, "already-paid",
                    $"The original transaction has already been accounted"));
            }
            await _btcPayWalletProvider.GetWallet(network).SaveOffchainTransactionAsync(originalTx);
            _eventAggregator.Publish(new InvoiceEvent(invoice, 1002, InvoiceEvent.ReceivedPayment) {Payment = payment});

            if (psbtFormat && HexEncoder.IsWellFormed(rawBody))
            {
                return Ok(newPsbt.ToHex());
            }
            else if (psbtFormat)
            {
                return Ok(newPsbt.ToBase64());
            }
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
