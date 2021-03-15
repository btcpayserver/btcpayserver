using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.BIP78.Sender;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Filters;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Payments.PayJoin.Receiver
{
    [Route("{cryptoCode}/" + PayjoinClient.BIP21EndpointKey)]
    public class PayJoinEndpointController : ControllerBase
    {
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly ExplorerClientProvider _explorerClientProvider;
        private readonly BTCPayWalletProvider _btcPayWalletProvider;
        private readonly EventAggregator _eventAggregator;
        private readonly BTCPayServerEnvironment _env;
        private readonly BTCPayPayjoinReceiverWallet _btcPayPayjoinReceiverWallet;

        public PayJoinEndpointController(BTCPayNetworkProvider btcPayNetworkProvider,
            InvoiceRepository invoiceRepository, ExplorerClientProvider explorerClientProvider,
            BTCPayWalletProvider btcPayWalletProvider,
            EventAggregator eventAggregator,
            BTCPayServerEnvironment env,
            BTCPayPayjoinReceiverWallet btcPayPayjoinReceiverWallet)
        {
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _invoiceRepository = invoiceRepository;
            _explorerClientProvider = explorerClientProvider;
            _btcPayWalletProvider = btcPayWalletProvider;
            _eventAggregator = eventAggregator;
            _env = env;
            _btcPayPayjoinReceiverWallet = btcPayPayjoinReceiverWallet;
        }

        [HttpPost("")]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        [MediaTypeConstraint("text/plain")]
        [RateLimitsFilter(ZoneLimits.PayJoin, Scope = RateLimitsScope.RemoteAddress)]
        public async Task<IActionResult> Submit(string cryptoCode,
            long? maxadditionalfeecontribution,
            int? additionalfeeoutputindex,
            decimal minfeerate = -1.0m,
            bool disableoutputsubstitution = false,
            int v = 1)
        {
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (network == null)
                return NotFound();

            if (v != 1)
            {
                return BadRequest(new JObject
                {
                    new JProperty("errorCode", "version-unsupported"),
                    new JProperty("supported", new JArray(1)),
                    new JProperty("message", "This version of payjoin is not supported.")
                });
            }

            var explorer = _explorerClientProvider.GetExplorerClient(network);
            if (Request.ContentLength is long length)
            {
                if (length > 1_000_000)
                    return this.StatusCode(413,
                        CreatePayjoinError("payload-too-large", "The transaction is too big to be processed"));
            }
            else
            {
                return StatusCode(411,
                    CreatePayjoinError("missing-content-length",
                        "The http header Content-Length should be filled"));
            }

            string rawBody;
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                rawBody = (await reader.ReadToEndAsync()) ?? string.Empty;
            }

            bool psbtFormat = true;

            if (!PSBT.TryParse(rawBody, network.NBitcoinNetwork, out var psbt))
            {
                psbtFormat = false;
                if (!Transaction.TryParse(rawBody, network.NBitcoinNetwork, out var tx))
                    return BadRequest(CreatePayjoinError("original-psbt-rejected", "invalid transaction or psbt"));
                psbt = PSBT.FromTransaction(tx, network.NBitcoinNetwork);
                psbt = (await explorer.UpdatePSBTAsync(new UpdatePSBTRequest() {PSBT = psbt})).PSBT;
                for (int i = 0; i < tx.Inputs.Count; i++)
                {
                    psbt.Inputs[i].FinalScriptSig = tx.Inputs[i].ScriptSig;
                    psbt.Inputs[i].FinalScriptWitness = tx.Inputs[i].WitScript;
                }
            }

            var ctx = new BTCPayPayjoinProposalContext(psbt, network, new PayjoinClientParameters()
            {
                Version = v,
                DisableOutputSubstitution = disableoutputsubstitution,
                MinFeeRate = minfeerate >= 0.0m ? new FeeRate(minfeerate) : null,
                MaxAdditionalFeeContribution =
                    Money.Satoshis(maxadditionalfeecontribution is long t && t >= 0 ? t : 0),
                AdditionalFeeOutputIndex = additionalfeeoutputindex
            });

            try
            {
                await _btcPayPayjoinReceiverWallet.Initiate(ctx);
            }
            catch (PayjoinReceiverException e)
            {
                if (ctx.Invoice != null)
                {
                    await _invoiceRepository.AddInvoiceLogs(ctx.Invoice.Id, ctx.InvoiceLogs);
                }

                return BadRequest(CreatePayjoinError(e.ErrorCode, e.ReceiverMessage));
            }

            // Add the transaction to the payments with a confirmation of -1.
            // This will make the invoice paid even if the user do not
            // broadcast the payjoin.
            var originalPaymentData = new BitcoinLikePaymentData(ctx.PaymentRequest.Destination,
                ctx.PaymentRequest.Amount,
                new OutPoint(ctx.OriginalTransaction.GetHash(), ctx.OriginalPaymentRequestOutput.Index),
                ctx.OriginalTransaction.RBF, ctx.PaymentMethodDetails.KeyPath);
            originalPaymentData.ConfirmationCount = -1;
            originalPaymentData.PayjoinInformation = new PayjoinInformation()
            {
                CoinjoinTransactionHash = ctx.PayjoinReceiverWalletProposal.PayjoinTransactionHash,
                CoinjoinValue = ctx.PayjoinReceiverWalletProposal.ModifiedPaymentRequest.Value,
                ContributedOutPoints = ctx.PayjoinReceiverWalletProposal.ContributedInputs.Select(o => o.Outpoint)
                    .ToArray()
            };
            var payment = await _invoiceRepository.AddPayment(ctx.Invoice.Id, DateTimeOffset.UtcNow,
                originalPaymentData, network, true);
            if (payment is null)
            {
                return UnprocessableEntity(CreatePayjoinError("already-paid",
                    $"The original transaction has already been accounted"));
            }

            await _btcPayWalletProvider.GetWallet(network).SaveOffchainTransactionAsync(ctx.OriginalTransaction);
            _eventAggregator.Publish(new InvoiceEvent(ctx.Invoice, InvoiceEvent.ReceivedPayment) {Payment = payment});
            _eventAggregator.Publish(new UpdateTransactionLabel()
            {
                WalletId = new WalletId(ctx.Invoice.StoreId, network.CryptoCode),
                TransactionLabels = ctx.PayjoinReceiverWalletProposal.ContributedInputs
                    .GroupBy(pair => pair.Outpoint.Hash).Select(utxo =>
                        new KeyValuePair<uint256, List<(string color, Label label)>>(utxo.Key,
                            new List<(string color, Label label)>()
                            {
                                UpdateTransactionLabel.PayjoinExposedLabelTemplate(ctx.Invoice.Id)
                            }))
                    .ToDictionary(pair => pair.Key, pair => pair.Value)
            });
            // BTCPay Server support PSBT set as hex
            if (psbtFormat && HexEncoder.IsWellFormed(rawBody))
            {
                return Ok(ctx.PayjoinReceiverWalletProposal.PayjoinPSBT.ToHex());
            }

            if (psbtFormat)
            {
                return Ok(ctx.PayjoinReceiverWalletProposal.PayjoinPSBT.ToBase64());
            }

            // BTCPay Server should returns transaction if received transaction
            return Ok(ctx.PayjoinReceiverWalletProposal.PayjoinPSBT.ExtractTransaction().ToHex());
        }
        private JObject CreatePayjoinError(string errorCode, string friendlyMessage)
        {
            var o = new JObject();
            o.Add(new JProperty("errorCode", errorCode));
            o.Add(new JProperty("message", friendlyMessage));
            return o;
        }
    }
}
