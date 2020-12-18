using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using BIP78.Receiver;
using BIP78.Sender;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.Crypto;
using NBXplorer;
using NBXplorer.Models;

namespace BTCPayServer.Payments.PayJoin.Receiver
{
    public class BTCPayPayjoinReceiverWallet : PayjoinReceiverWallet<BTCPayPayjoinProposalContext>
    {
        private readonly PayJoinRepository _payJoinRepository;
        private readonly ExplorerClientProvider _explorerClientProvider;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly DelayedTransactionBroadcaster _broadcaster;
        private readonly NBXplorerDashboard _nbXplorerDashboard;

        public BTCPayPayjoinReceiverWallet(PayJoinRepository payJoinRepository,
            ExplorerClientProvider explorerClientProvider,
            InvoiceRepository invoiceRepository,
            DelayedTransactionBroadcaster broadcaster,
            NBXplorerDashboard nbXplorerDashboard)
        {
            _payJoinRepository = payJoinRepository;
            _explorerClientProvider = explorerClientProvider;
            _invoiceRepository = invoiceRepository;
            _broadcaster = broadcaster;
            _nbXplorerDashboard = nbXplorerDashboard;
        }

        protected override Task BroadcastOriginalTransaction(BTCPayPayjoinProposalContext context, TimeSpan timeSpan)
        {
            return timeSpan == TimeSpan.Zero
                ? _explorerClientProvider.GetExplorerClient(context.Network).BroadcastAsync(context.OriginalTransaction)
                : _broadcaster.Schedule(DateTimeOffset.Now.Add(timeSpan), context.OriginalTransaction, context.Network);
        }

        protected override Task<bool> SupportsType(ScriptPubKeyType scriptPubKeyType)
        {
            return Task.FromResult(scriptPubKeyType != ScriptPubKeyType.Legacy);
        }

        protected override async Task<bool> InputsSeenBefore(PSBTInputList inputList)
        {
            return ! await _payJoinRepository.TryLockInputs(inputList.Select(input => input.PrevOut).ToArray());
        }

        protected override async Task<string> IsMempoolEligible(PSBT psbt)
        {
            var explorerClient = _explorerClientProvider.GetExplorerClient(psbt.Network.NetworkSet.CryptoCode);
            var result = await explorerClient.BroadcastAsync(psbt.ExtractTransaction(), true);
            return result.Success ? null : result.RPCCodeMessage;
        }

        protected override async Task ComputePayjoinModifications(BTCPayPayjoinProposalContext context)
        {
            var enforcedLowR = context.OriginalPSBT.Inputs.All(IsLowR);
            Money due = context.PaymentRequest.Amount;
            Dictionary<OutPoint, UTXO> selectedUTXOs = new Dictionary<OutPoint, UTXO>();
            var utxos = (await _explorerClientProvider.GetExplorerClient(context.Network)
                    .GetUTXOsAsync(context.PaymentMethod.AccountDerivation))
                .GetUnspentUTXOs(false);
            // In case we are paying ourselves, be need to make sure
            // we can't take spent outpoints.
            var prevOuts = context.OriginalTransaction.Inputs.Select(o => o.PrevOut).ToHashSet();
            utxos = utxos.Where(u => !prevOuts.Contains(u.Outpoint)).ToArray();
            Array.Sort(utxos, UTXODeterministicComparer.Instance);
            foreach (var utxo in (await SelectUTXO(context.Network, utxos,
                context.OriginalPSBT.Inputs.Select(input => input.WitnessUtxo.Value.ToDecimal(MoneyUnit.BTC)),
                context.OriginalPaymentRequestOutput.Value.ToDecimal(MoneyUnit.BTC),
                context.OriginalPSBT.Outputs
                    .Where(psbtOutput => psbtOutput.Index != context.OriginalPaymentRequestOutput.Index)
                    .Select(psbtOutput => psbtOutput.Value.ToDecimal(MoneyUnit.BTC)))).selectedUTXO)
            {
                selectedUTXOs.Add(utxo.Outpoint, utxo);
            }

            if (selectedUTXOs.Count == 0)
            {
                return;
            }

            Money contributedAmount = Money.Zero;
            var newTx = context.OriginalTransaction.Clone();
            var ourNewOutput = newTx.Outputs[context.OriginalPaymentRequestOutput.Index];
            HashSet<TxOut> isOurOutput = new HashSet<TxOut>();
            isOurOutput.Add(ourNewOutput);
            TxOut feeOutput =
                context.PayjoinParameters.AdditionalFeeOutputIndex is int feeOutputIndex &&
                context.PayjoinParameters.MaxAdditionalFeeContribution > Money.Zero &&
                feeOutputIndex >= 0
                && feeOutputIndex < newTx.Outputs.Count
                && !isOurOutput.Contains(newTx.Outputs[feeOutputIndex])
                    ? newTx.Outputs[feeOutputIndex]
                    : null;
            var rand = new Random();
            int senderInputCount = newTx.Inputs.Count;
            foreach (var selectedUTXO in selectedUTXOs.Select(o => o.Value))
            {
                contributedAmount += (Money)selectedUTXO.Value;
                var newInput = newTx.Inputs.Add(selectedUTXO.Outpoint);
                newInput.Sequence = newTx.Inputs[rand.Next(0, senderInputCount)].Sequence;
            }

            ourNewOutput.Value += contributedAmount;
            var minRelayTxFee =
                _nbXplorerDashboard.Get(context.Network.CryptoCode).Status.BitcoinStatus?.MinRelayTxFee ??
                new FeeRate(1.0m);

            // Remove old signatures as they are not valid anymore
            foreach (var input in newTx.Inputs)
            {
                input.WitScript = WitScript.Empty;
            }

            Money ourFeeContribution = Money.Zero;
            // We need to adjust the fee to keep a constant fee rate
            var txBuilder = context.Network.NBitcoinNetwork.CreateTransactionBuilder();
            var coins = context.OriginalPSBT.Inputs.Select(i => i.GetSignableCoin())
                .Concat(selectedUTXOs.Select(o => o.Value.AsCoin(context.PaymentMethod.AccountDerivation))).ToArray();

            txBuilder.AddCoins(coins);
            Money expectedFee = txBuilder.EstimateFees(newTx, context.OriginalPSBT.GetEstimatedFeeRate());
            Money actualFee = newTx.GetFee(txBuilder.FindSpentCoins(newTx));
            Money additionalFee = expectedFee - actualFee;
            bool notEnoughMoney = false;
            Money feeFromOutputIndex = Money.Zero;
            if (additionalFee > Money.Zero)
            {
                // If the user overpaid, taking fee on our output (useful if sender dump a full UTXO for privacy)
                for (int i = 0; i < newTx.Outputs.Count && additionalFee > Money.Zero && due < Money.Zero; i++)
                {
                    if (context.PayjoinParameters.DisableOutputSubstitution is true)
                        break;
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
                if (feeOutput != null)
                {
                    var outputContribution = Money.Min(additionalFee, feeOutput.Value);
                    outputContribution = Money.Min(outputContribution,
                        feeOutput.Value - feeOutput.GetDustThreshold(minRelayTxFee));
                    outputContribution = Money.Min(outputContribution,
                        context.PayjoinParameters.MaxAdditionalFeeContribution);
                    feeOutput.Value -= outputContribution;

                    additionalFee -= outputContribution;
                    feeFromOutputIndex = outputContribution;
                }

                if (additionalFee > Money.Zero)
                {
                    // We could not pay fully the additional fee, however, as long as
                    // we are not under the relay fee, it should be OK.
                    var newVSize = txBuilder.EstimateSize(newTx, true);
                    var newFeePaid = newTx.GetFee(txBuilder.FindSpentCoins(newTx));
                    if (new FeeRate(newFeePaid, newVSize) < (context.PayjoinParameters.MinFeeRate ?? minRelayTxFee))
                    {
                        notEnoughMoney = true;
                    }
                }
            }

            if (!notEnoughMoney)
            {
                var accountKey = context.SigningKey;
                var newPsbt = PSBT.FromTransaction(newTx, context.Network.NBitcoinNetwork);
                foreach (var selectedUtxo in selectedUTXOs.Select(o => o.Value))
                {
                    var signedInput = newPsbt.Inputs.FindIndexedInput(selectedUtxo.Outpoint);
                    var coin = selectedUtxo.AsCoin(context.PaymentMethod.AccountDerivation);
                    signedInput.UpdateFromCoin(coin);
                    var privateKey = accountKey.Derive(selectedUtxo.KeyPath).PrivateKey;
                    signedInput.Sign(privateKey, new SigningOptions() {EnforceLowR = enforcedLowR});
                    signedInput.FinalizeInput();
                    newTx.Inputs[signedInput.Index].WitScript =
                        newPsbt.Inputs[(int)signedInput.Index].FinalScriptWitness;
                }


                context.PayjoinReceiverWalletProposal = new PayjoinReceiverWalletProposal()
                {
                    PayjoinPSBT = newPsbt,
                    ContributedInputs =
                        selectedUTXOs.Select(pair => pair.Value.AsCoin(context.PaymentMethod.AccountDerivation))
                            .ToArray(),
                    ContributedOutputs = Array.Empty<TxOut>(),
                    ModifiedPaymentRequest = ourNewOutput,
                    ExtraFeeFromAdditionalFeeOutput = feeFromOutputIndex,
                    ExtraFeeFromReceiverInputs = ourFeeContribution
                };
            }

            if (context.PayjoinReceiverWalletProposal is null)
                await _payJoinRepository.TryUnlock(
                    context.OriginalPSBT.Inputs.Select(input => input.PrevOut).Concat(selectedUTXOs.Select(pair => pair.Key)).ToArray()
                    .ToArray());
            if (notEnoughMoney)
            {
                throw new PayjoinReceiverException(
                    PayjoinReceiverHelper.GetErrorCode(PayjoinReceiverWellknownErrors.NotEnoughMoney),
                    "Not enough money is sent to pay for the additional payjoin inputs");
            }
        }

        protected override async Task<PayjoinPaymentRequest> FindMatchingPaymentRequests(
            BTCPayPayjoinProposalContext context)
        {
            var receiverInputsType = context.OriginalPSBT.GetInputsScriptPubKeyType();
            var alreadyPaid = false;
            var inputMismatch = false;
            foreach (var output in context.OriginalPSBT.Outputs)
            {
                var paymentMethodId = new PaymentMethodId(context.Network.CryptoCode.ToUpperInvariant(),
                    BitcoinPaymentType.Instance);
                var key = output.ScriptPubKey.Hash + "#" + paymentMethodId.CryptoCode;
                var invoice = (await _invoiceRepository.GetInvoicesFromAddresses(new[] {key})).FirstOrDefault();
                var paymentMethod = invoice?.GetPaymentMethod(paymentMethodId);
                if (paymentMethod?.GetPaymentMethodDetails() is BitcoinLikeOnChainPaymentMethod
                    {PayjoinEnabled: true} paymentMethodDetails)
                {
                    var derivationSchemeSettings = invoice
                        .GetSupportedPaymentMethod<DerivationSchemeSettings>(paymentMethodId)
                        .SingleOrDefault();
                    if (derivationSchemeSettings is null)
                        continue;
                    var sendersInputType = derivationSchemeSettings.AccountDerivation.ScriptPubKeyType();
                    if (sendersInputType is ScriptPubKeyType t1 && t1 != receiverInputsType)
                    {
                        inputMismatch = true;
                        continue;
                        ;
                    }

                    if (invoice.GetAllBitcoinPaymentData().Any() ||
                        paymentMethod.Calculate().TotalDue != paymentMethod.Calculate().Due)
                    {
                        alreadyPaid = true;
                        continue;
                    }

                    var extKeyStr = await _explorerClientProvider.GetExplorerClient(context.Network)
                        .GetMetadataAsync<string>(
                            derivationSchemeSettings.AccountDerivation,
                            WellknownMetadataKeys.AccountHDKey);
                    if (extKeyStr == null)
                    {
                        continue;
                    }

                    context.SigningKey = ExtKey.Parse(extKeyStr, context.Network.NBitcoinNetwork);
                    context.PaymentMethod = derivationSchemeSettings;
                    context.Invoice = invoice;
                    context.PaymentMethodDetails = paymentMethodDetails;
                    return new PayjoinPaymentRequest
                    {
                        Amount = paymentMethod.Calculate().Due,
                        Destination = paymentMethodDetails.GetDepositAddress(context.OriginalPSBT.Network)
                    };
                }
            }

            if (alreadyPaid)
            {
                throw new PayjoinReceiverException("already-paid",
                    $"The invoice this PSBT is paying has already been partially or completely paid");
            }

            if (inputMismatch)
            {
                throw new PayjoinReceiverException(
                    PayjoinReceiverHelper.GetErrorCode(PayjoinReceiverWellknownErrors.Unavailable),
                    "We do not have any UTXO available for making a payjoin with the sender's inputs type");
            }

            return null;
        }


        public enum PayjoinUtxoSelectionType
        {
            Unavailable,
            HeuristicBased,
            Ordered
        }

        [NonAction]
        private async Task<(UTXO[] selectedUTXO, PayjoinUtxoSelectionType selectionType)> SelectUTXO(
            BTCPayNetwork network, UTXO[] availableUtxos, IEnumerable<decimal> otherInputs, decimal mainPaymentOutput,
            IEnumerable<decimal> otherOutputs)
        {
            if (availableUtxos.Length == 0)
                return (Array.Empty<UTXO>(), PayjoinUtxoSelectionType.Unavailable);
            // Assume the merchant wants to get rid of the dust
            HashSet<OutPoint> locked = new HashSet<OutPoint>();
            // We don't want to make too many db roundtrip which would be inconvenient for the sender
            int maxTries = 30;
            int currentTry = 0;
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

                var invalid = false;
                foreach (var input in otherInputs.Concat(new[] {availableUtxo.Value.GetValue(network)}))
                {
                    var computedOutputs =
                        otherOutputs.Concat(new[] {mainPaymentOutput + availableUtxo.Value.GetValue(network)});
                    if (computedOutputs.Any(output => input > output))
                    {
                        //UIH 1 & 2
                        invalid = true;
                        break;
                    }
                }

                if (invalid)
                {
                    continue;
                }

                if (await _payJoinRepository.TryLock(availableUtxo.Outpoint))
                {
                    return (new[] {availableUtxo}, PayjoinUtxoSelectionType.HeuristicBased);
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
                    return (new[] {utxo}, PayjoinUtxoSelectionType.Ordered);
                }

                currentTry++;
            }

            return (Array.Empty<UTXO>(), PayjoinUtxoSelectionType.Unavailable);
        }

        private static bool IsLowR(PSBTInput txin)
        {
            IEnumerable<byte[]> pushes = txin.FinalScriptWitness?.PushCount > 0 ? txin.FinalScriptWitness.Pushes :
                txin.FinalScriptSig?.IsPushOnly is true ? txin.FinalScriptSig.ToOps().Select(o => o.PushData) :
                Array.Empty<byte[]>();
            return pushes.Where(ECDSASignature.IsValidDER).All(p => p.Length <= 71);
        }

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

            static readonly UTXODeterministicComparer _Instance;
            private readonly byte[] _blind;

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
    }
}
