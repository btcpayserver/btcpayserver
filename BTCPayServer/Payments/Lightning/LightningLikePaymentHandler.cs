#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.LndHub;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Security;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments.Lightning
{
    public interface ILightningPaymentHandler : IHasNetwork
    {
        LightningPaymentData ParsePaymentDetails(JToken details);
    }
    public class LightningLikePaymentHandler : IPaymentMethodHandler, ILightningPaymentHandler
    {
        public JsonSerializer Serializer { get; }
        public static readonly int LightningTimeout = 5000;
        readonly NBXplorerDashboard _Dashboard;
        private readonly LightningClientFactoryService _lightningClientFactory;
        private readonly BTCPayNetwork _Network;
        private readonly SocketFactory _socketFactory;
        private readonly ISettingsAccessor<PoliciesSettings> _policies;
        private readonly IOptions<LightningNetworkOptions> _lightningNetworkOptions;

        public LightningLikePaymentHandler(
            PaymentMethodId paymentMethodId,
            NBXplorerDashboard dashboard,
            LightningClientFactoryService lightningClientFactory,
            BTCPayNetwork network,
            SocketFactory socketFactory,
            IOptions<LightningNetworkOptions> options,
            ISettingsAccessor<PoliciesSettings> policies,
            IOptions<LightningNetworkOptions> lightningNetworkOptions)
        {
            Serializer = BlobSerializer.CreateSerializer(network.NBitcoinNetwork).Serializer;
            _Dashboard = dashboard;
            _lightningClientFactory = lightningClientFactory;
            _Network = network;
            _socketFactory = socketFactory;
            Options = options;
            _policies = policies;
            _lightningNetworkOptions = lightningNetworkOptions;
            PaymentMethodId = paymentMethodId;
        }

        public Task BeforeFetchingRates(PaymentMethodContext context)
        {
            context.Prompt.Currency = _Network.CryptoCode;
            context.Prompt.PaymentMethodFee = 0m;
            context.Prompt.Divisibility = 11;
            return Task.CompletedTask;
        }

        public PaymentMethodId PaymentMethodId { get; private set; }

        public IOptions<LightningNetworkOptions> Options { get; }

        public BTCPayNetwork Network => _Network;
        static LightMoney OneSat = LightMoney.FromUnit(1.0m, LightMoneyUnit.Satoshi);
        public async Task ConfigurePrompt(PaymentMethodContext context)
        {
            if (context.InvoiceEntity.Type == InvoiceType.TopUp)
            {
                throw new PaymentMethodUnavailableException("Lightning Network payment method is not available for top-up invoices");
            }

            var paymentPrompt = context.Prompt;

            var preferOnion = Uri.TryCreate(context.InvoiceEntity.ServerUrl, UriKind.Absolute, out var u) && u.IsOnion();

            var storeBlob = context.StoreBlob;
            var store = context.Store;

            var config = ParsePaymentMethodConfig(context.PaymentMethodConfig);
            var nodeInfo = GetNodeInfo(config, context.Logs, preferOnion);

            var invoice = context.InvoiceEntity;
            decimal due = paymentPrompt.Calculate().Due;
            var client = config.CreateLightningClient(_Network, Options.Value, _lightningClientFactory);
            var expiry = invoice.ExpirationTime - DateTimeOffset.UtcNow;
            if (expiry < TimeSpan.Zero)
                expiry = TimeSpan.FromSeconds(1);

            LightningInvoice? lightningInvoice;

            string description = storeBlob.LightningDescriptionTemplate;
            description = description.Replace("{StoreName}", store.StoreName ?? "", StringComparison.OrdinalIgnoreCase)
                                     .Replace("{ItemDescription}", invoice.Metadata.ItemDesc ?? "", StringComparison.OrdinalIgnoreCase)
                                     .Replace("{OrderId}", invoice.Metadata.OrderId ?? "", StringComparison.OrdinalIgnoreCase);
            using (var cts = new CancellationTokenSource(LightningTimeout))
            {
                try
                {
                    var request = new CreateInvoiceParams(new LightMoney(due, LightMoneyUnit.BTC), description, expiry);
                    request.PrivateRouteHints = storeBlob.LightningPrivateRouteHints;
                    lightningInvoice = await client.CreateInvoice(request, cts.Token);
                    var diff = request.Amount - lightningInvoice.Amount;
                    if (diff != LightMoney.Zero)
                    {
                        // Some providers doesn't round up to msat. So we tweak the fees so the due match the BOLT11's amount.
                        paymentPrompt.AddTweakFee(-diff.ToUnit(LightMoneyUnit.BTC));
                    }
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    throw new PaymentMethodUnavailableException("The lightning node did not reply in a timely manner");
                }
                catch (Exception ex)
                {
                    throw new PaymentMethodUnavailableException($"Impossible to create lightning invoice ({ex.Message})", ex);
                }
            }

            paymentPrompt.Destination = lightningInvoice.BOLT11;
            var details = new LigthningPaymentPromptDetails
            {
                PaymentHash = BOLT11PaymentRequest.Parse(lightningInvoice.BOLT11, _Network.NBitcoinNetwork).PaymentHash,
                Preimage = string.IsNullOrEmpty(lightningInvoice.Preimage) ? null : uint256.Parse(lightningInvoice.Preimage),
                InvoiceId = lightningInvoice.Id,
                NodeInfo = (await nodeInfo).FirstOrDefault()?.ToString()
            };
            paymentPrompt.Details = JObject.FromObject(details, Serializer);
        }


        public async Task<NodeInfo[]> GetNodeInfo(LightningPaymentMethodConfig supportedPaymentMethod, PrefixedInvoiceLogs? invoiceLogs, bool? preferOnion = null, bool throws = false)
        {
            var synced = _Dashboard.IsFullySynched(_Network.CryptoCode, out var summary);
            if (supportedPaymentMethod.IsInternalNode && !synced)
                throw new PaymentMethodUnavailableException("Full node not available");

            try
            {
                using var cts = new CancellationTokenSource(LightningTimeout);
                var client = CreateLightningClient(supportedPaymentMethod);

                // LNDhub-compatible implementations might not offer all of GetInfo data.
                // Skip checks in those cases, see https://github.com/lnbits/lnbits/issues/1182
                var isLndHub = client is LndHubLightningClient;

                LightningNodeInformation info;
                try
                {
                    info = await client.GetInfo(cts.Token);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    throw new PaymentMethodUnavailableException("The lightning node did not reply in a timely manner");
                }
                catch (NotSupportedException)
                {
                    // LNDhub, LNbits and others might not support this call, yet we can create invoices.
                    return new NodeInfo[] { };
                }
                catch (UnauthorizedAccessException)
                {
                    // LND might return this with restricted macaroon, support this nevertheless..
                    return new NodeInfo[] { };
                }
                catch (Exception ex)
                {
                    throw new PaymentMethodUnavailableException($"Error while connecting to the API: {ex.Message}" +
                                                                (!string.IsNullOrEmpty(ex.InnerException?.Message) ? $" ({ex.InnerException.Message})" : ""));
                }

                // Node info might be empty if there are no public URIs to announce. The UI also supports this.
                var nodeInfo = preferOnion != null && info.NodeInfoList.Any(i => i.IsTor == preferOnion)
                    ? info.NodeInfoList.Where(i => i.IsTor == preferOnion.Value).ToArray()
                    : info.NodeInfoList.Select(i => i).ToArray();

                if (summary.Status is not null)
                {
                    var blocksGap = summary.Status.ChainHeight - info.BlockHeight;
                    if (blocksGap > 10 && !(isLndHub && info.BlockHeight == 0))
                    {
                        throw new PaymentMethodUnavailableException(
                            $"The lightning node is not synched ({blocksGap} blocks left)");
                    }
                }
                return nodeInfo;
            }
            catch (Exception e) when (!throws)
            {
                invoiceLogs?.Write($"NodeInfo failed to be fetched: {e.Message}", InvoiceEventData.EventSeverity.Error);
            }

            return Array.Empty<NodeInfo>();
        }

        public ILightningClient CreateLightningClient(LightningPaymentMethodConfig supportedPaymentMethod)
        {
            return supportedPaymentMethod.CreateLightningClient(_Network, Options.Value, _lightningClientFactory);
        }

        public async Task TestConnection(NodeInfo nodeInfo, CancellationToken cancellation)
        {
            try
            {
                if (!Utils.TryParseEndpoint(nodeInfo.Host, nodeInfo.Port, out var endpoint))
                    throw new PaymentMethodUnavailableException($"Could not parse the endpoint {nodeInfo.Host}");

                using var tcp = await _socketFactory.ConnectAsync(endpoint, cancellation);
            }
            catch (Exception ex)
            {
                throw new PaymentMethodUnavailableException($"Error while connecting to the lightning node via {nodeInfo.Host}:{nodeInfo.Port} ({ex.Message})");
            }
        }
        public LightningPaymentMethodConfig ParsePaymentMethodConfig(JToken config)
        {
            return config.ToObject<LightningPaymentMethodConfig>(Serializer) ?? throw new FormatException($"Invalid {nameof(LightningPaymentMethodConfig)}");
        }
        object IPaymentMethodHandler.ParsePaymentMethodConfig(JToken config)
        {
            return ParsePaymentMethodConfig(config);
        }
        object IPaymentMethodHandler.ParsePaymentPromptDetails(JToken details)
        {
            return ParsePaymentPromptDetails(details);
        }
        public LigthningPaymentPromptDetails ParsePaymentPromptDetails(JToken details)
        {
            return details.ToObject<LigthningPaymentPromptDetails>(Serializer) ?? throw new FormatException($"Invalid {nameof(LigthningPaymentPromptDetails)}");
        }
        public LightningPaymentData ParsePaymentDetails(JToken details)
        {
            return details.ToObject<LightningPaymentData>(Serializer) ?? throw new FormatException($"Invalid {nameof(LightningPaymentData)}");
        }
        object IPaymentMethodHandler.ParsePaymentDetails(JToken details)
        {
            return ParsePaymentDetails(details);
        }
        public async Task ValidatePaymentMethodConfig(PaymentMethodConfigValidationContext validationContext)
        {
            if (validationContext.Config is JValue { Type: JTokenType.String })
                validationContext.Config = new JObject() { ["connectionString"] = validationContext.Config.Value<string>()! };
#pragma warning disable CS0618 // Type or member is obsolete
            var config = ParsePaymentMethodConfig(validationContext.Config);
            if (config.ConnectionString == LightningPaymentMethodConfig.InternalNode)
                config.SetInternalNode();
            LightningPaymentMethodConfig? oldConfig = null;
            if (validationContext.PreviousConfig is not null)
                oldConfig = ParsePaymentMethodConfig(validationContext.PreviousConfig);
            var connectionStringChanged = oldConfig?.ConnectionString != config.ConnectionString;
            if (connectionStringChanged && !string.IsNullOrEmpty(config.ConnectionString))
            {
                // Let's check the connection string can be parsed and is safe to use for non-admin.
                try
                {
                    var client = _lightningClientFactory.Create(config.ConnectionString, _Network);
                    if (!client.IsSafe())
                    {
                        var canManage = (await validationContext.AuthorizationService.AuthorizeAsync(validationContext.User, null,
                    new PolicyRequirement(Policies.CanModifyServerSettings))).Succeeded;
                        if (!canManage)
                        {
                            validationContext.ModelState.AddModelError(nameof(config.ConnectionString), $"You do not have 'btcpay.server.canmodifyserversettings' rights, so the connection string should not contain 'cookiefilepath', 'macaroondirectorypath', 'macaroonfilepath', and should not point to a local ip or to a dns name ending with '.internal', '.local', '.lan' or '.'.");
                            return;
                        }
                    }
                }
                catch
                {
                    validationContext.ModelState.AddModelError(nameof(config.ConnectionString), "Invalid connection string");
                    return;
                }
            }

            if (oldConfig?.IsInternalNode != config.IsInternalNode && config.IsInternalNode)
            {
                var canUseInternalNode = _policies.Settings.AllowLightningInternalNodeForAll ||
                   (await validationContext.AuthorizationService.AuthorizeAsync(validationContext.User, null,
                       new PolicyRequirement(Policies.CanUseInternalLightningNode))).Succeeded && _lightningNetworkOptions.Value.InternalLightningByCryptoCode.ContainsKey(_Network.CryptoCode);
                if (!canUseInternalNode)
                {
                    validationContext.SetMissingPermission(Policies.CanUseInternalLightningNode, $"You are not authorized to use the internal lightning node. Either add '{Policies.CanUseInternalLightningNode}' to an API Key, or allow non-admin users to use the internal lightning node in the server settings.");
                    return;
                }
            }

            if (!config.IsInternalNode && string.IsNullOrEmpty(config.ConnectionString))
            {
                validationContext.ModelState.AddModelError(nameof(config.ConnectionString), "The connection string or setting the internal node is required");
                return;
            }
            validationContext.Config = JToken.FromObject(config, Serializer);
#pragma warning restore CS0618 // Type or member is obsolete
        }
    }
}
