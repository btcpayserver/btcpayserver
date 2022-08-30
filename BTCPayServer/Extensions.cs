using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.BIP78.Sender;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Security;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Payment;
using NBXplorer.Models;
using Newtonsoft.Json;
using InvoiceCryptoInfo = BTCPayServer.Services.Invoices.InvoiceCryptoInfo;

namespace BTCPayServer
{
    public static class Extensions
    {
        public static bool IsValidEmail(this string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return false;
            }

            return MailboxAddressValidator.TryParse(email, out var ma) && ma.ToString() == ma.Address;
        }
        
        public static bool TryGetPayjoinEndpoint(this BitcoinUrlBuilder bip21, out Uri endpoint)
        {
            endpoint = bip21.UnknownParameters.TryGetValue($"{PayjoinClient.BIP21EndpointKey}", out var uri) ? new Uri(uri, UriKind.Absolute) : null;
            return endpoint != null;
        }

        public static bool IsSafe(this LightningConnectionString connectionString)
        {
            if (connectionString.CookieFilePath != null ||
                connectionString.MacaroonDirectoryPath != null ||
                connectionString.MacaroonFilePath != null)
                return false;

            var uri = connectionString.BaseUri;
            if (uri.Scheme.Equals("unix", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!NBitcoin.Utils.TryParseEndpoint(uri.DnsSafeHost, 80, out var endpoint))
                return false;
            return !Extensions.IsLocalNetwork(uri.DnsSafeHost);
        }

        public static IQueryable<TEntity> Where<TEntity>(this Microsoft.EntityFrameworkCore.DbSet<TEntity> obj, System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate) where TEntity : class
        {
            return System.Linq.Queryable.Where(obj, predicate);
        }

        public static string PrettyPrint(this TimeSpan expiration)
        {
            StringBuilder builder = new StringBuilder();
            if (expiration.Days >= 1)
                builder.Append(expiration.Days.ToString(CultureInfo.InvariantCulture));
            if (expiration.Hours >= 1)
                builder.Append(expiration.Hours.ToString("00", CultureInfo.InvariantCulture));
            builder.Append(CultureInfo.InvariantCulture, $"{expiration.Minutes.ToString("00", CultureInfo.InvariantCulture)}:{expiration.Seconds.ToString("00", CultureInfo.InvariantCulture)}");
            return builder.ToString();
        }
        
        public static decimal RoundUp(decimal value, int precision)
        {
            for (int i = 0; i < precision; i++)
            {
                value = value * 10m;
            }
            value = Math.Ceiling(value);
            for (int i = 0; i < precision; i++)
            {
                value = value / 10m;
            }
            return value;
        }

        public static PaymentMethodId GetpaymentMethodId(this InvoiceCryptoInfo info)
        {
            return new PaymentMethodId(info.CryptoCode, PaymentTypes.Parse(info.PaymentType));
        }
        
        public static async Task CloseSocket(this WebSocket webSocket)
        {
            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    using CancellationTokenSource cts = new CancellationTokenSource();
                    cts.CancelAfter(5000);
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cts.Token);
                }
            }
            catch { }
            finally { try { webSocket.Dispose(); } catch { } }
        }

        public static IEnumerable<BitcoinLikePaymentData> GetAllBitcoinPaymentData(this InvoiceEntity invoice, bool accountedOnly)
        {
            return invoice.GetPayments(accountedOnly)
                .Where(p => p.GetPaymentMethodId()?.PaymentType == PaymentTypes.BTCLike)
                .Select(p => (BitcoinLikePaymentData)p.GetCryptoPaymentData())
                .Where(data => data != null);
        }

        public static async Task<Dictionary<uint256, TransactionResult>> GetTransactions(this BTCPayWallet client, uint256[] hashes, bool includeOffchain = false, CancellationToken cts = default(CancellationToken))
        {
            hashes = hashes.Distinct().ToArray();
            var transactions = hashes
                                        .Select(async o => await client.GetTransactionAsync(o, includeOffchain, cts))
                                        .ToArray();
            await Task.WhenAll(transactions).ConfigureAwait(false);
            return transactions.Select(t => t.Result).Where(t => t != null).ToDictionary(o => o.Transaction.GetHash());
        }

#nullable enable
        public static IPayoutHandler? FindPayoutHandler(this IEnumerable<IPayoutHandler> handlers, PaymentMethodId paymentMethodId)
        {
            return handlers.FirstOrDefault(h => h.CanHandle(paymentMethodId));
        }
#nullable restore

        public static async Task<PSBT> UpdatePSBT(this ExplorerClientProvider explorerClientProvider, DerivationSchemeSettings derivationSchemeSettings, PSBT psbt)
        {
            var result = await explorerClientProvider.GetExplorerClient(psbt.Network.NetworkSet.CryptoCode).UpdatePSBTAsync(new UpdatePSBTRequest()
            {
                PSBT = psbt,
                DerivationScheme = derivationSchemeSettings.AccountDerivation,
                AlwaysIncludeNonWitnessUTXO = true
            });
            if (result == null)
                return null;
            derivationSchemeSettings.RebaseKeyPaths(result.PSBT);
            return result.PSBT;
        }

        public static void SetHeaderOnStarting(this HttpResponse resp, string name, string value)
        {
            if (resp.HasStarted)
                return;
            resp.OnStarting(() =>
            {
                SetHeader(resp, name, value);
                return Task.CompletedTask;
            });
        }

        public static void SetHeader(this HttpResponse resp, string name, string value)
        {
            var existing = resp.Headers[name].FirstOrDefault();
            if (existing != null && value == null)
                resp.Headers.Remove(name);
            else
                resp.Headers[name] = value;
        }

        public static bool IsLocalNetwork(string server)
        {
            ArgumentNullException.ThrowIfNull(server);
            if (Uri.CheckHostName(server) == UriHostNameType.Dns)
            {
                return server.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
                   server.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                   server.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) ||
                   server.IndexOf('.', StringComparison.OrdinalIgnoreCase) == -1;
            }
            if (IPAddress.TryParse(server, out var ip))
            {
                return ip.IsLocal() || ip.IsRFC1918();
            }
            return false;
        }

        public static bool IsOnion(this Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri)
                return false;
            return uri.DnsSafeHost.EndsWith(".onion", StringComparison.OrdinalIgnoreCase);
        }

        public static string GetSIN(this ClaimsPrincipal principal)
        {
            return principal.Claims.Where(c => c.Type == Security.Bitpay.BitpayClaims.SIN).Select(c => c.Value).FirstOrDefault();
        }

        public static void SetIsBitpayAPI(this HttpContext ctx, bool value)
        {
            NBitcoin.Extensions.TryAdd(ctx.Items, "IsBitpayAPI", value);
        }

        public static bool GetIsBitpayAPI(this HttpContext ctx)
        {
            return ctx.Items.TryGetValue("IsBitpayAPI", out object obj) &&
                  obj is bool b && b;
        }

        public static void SetBitpayAuth(this HttpContext ctx, (string Signature, String Id, String Authorization) value)
        {
            NBitcoin.Extensions.TryAdd(ctx.Items, "BitpayAuth", value);
        }

        public static bool TryGetBitpayAuth(this HttpContext ctx, out (string Signature, String Id, String Authorization) result)
        {
            if (ctx.Items.TryGetValue("BitpayAuth", out object obj))
            {
                result = ((string Signature, String Id, String Authorization))obj;
                return true;
            }
            result = default;
            return false;
        }

        public static UserPrefsCookie GetUserPrefsCookie(this HttpContext ctx)
        {
            var prefCookie = new UserPrefsCookie();
            ctx.Request.Cookies.TryGetValue(nameof(UserPrefsCookie), out var strPrefCookie);
            if (!string.IsNullOrEmpty(strPrefCookie))
            {
                try
                {
                    prefCookie = JsonConvert.DeserializeObject<UserPrefsCookie>(strPrefCookie);
                }
                catch { /* ignore cookie deserialization failures */ }
            }

            return prefCookie;
        }

        public static void DeleteUserPrefsCookie(this HttpContext ctx)
        {
            ctx.Response.Cookies.Delete(nameof(UserPrefsCookie));
        }

        private static void SetCurrentStoreId(this HttpContext ctx, string storeId)
        {
            var prefCookie = ctx.GetUserPrefsCookie();
            if (prefCookie.CurrentStoreId != storeId)
            {
                prefCookie.CurrentStoreId = storeId;
                ctx.Response.Cookies.Append(nameof(UserPrefsCookie), JsonConvert.SerializeObject(prefCookie));
            }
        }

        public static string GetCurrentStoreId(this HttpContext ctx)
        {
            return ctx.GetImplicitStoreId() ?? ctx.GetUserPrefsCookie()?.CurrentStoreId;
        }

        public static StoreData GetStoreData(this HttpContext ctx)
        {
            return ctx.Items.TryGet("BTCPAY.STOREDATA") as StoreData;
        }

        public static void SetStoreData(this HttpContext ctx, StoreData storeData)
        {
            ctx.Items["BTCPAY.STOREDATA"] = storeData;

            SetCurrentStoreId(ctx, storeData.Id);
        }

        public static StoreData[] GetStoresData(this HttpContext ctx)
        {
            return ctx.Items.TryGet("BTCPAY.STORESDATA") as StoreData[];
        }

        public static void SetStoresData(this HttpContext ctx, StoreData[] storeData)
        {
            ctx.Items["BTCPAY.STORESDATA"] = storeData;
        }

        public static InvoiceEntity GetInvoiceData(this HttpContext ctx)
        {
            return ctx.Items.TryGet("BTCPAY.INVOICEDATA") as InvoiceEntity;
        }

        public static void SetInvoiceData(this HttpContext ctx, InvoiceEntity invoiceEntity)
        {
            ctx.Items["BTCPAY.INVOICEDATA"] = invoiceEntity;
        }

        public static PaymentRequestData GetPaymentRequestData(this HttpContext ctx)
        {
            return ctx.Items.TryGet("BTCPAY.PAYMENTREQUESTDATA") as PaymentRequestData;
        }

        public static void SetPaymentRequestData(this HttpContext ctx, PaymentRequestData paymentRequestData)
        {
            ctx.Items["BTCPAY.PAYMENTREQUESTDATA"] = paymentRequestData;
        }

        public static AppData GetAppData(this HttpContext ctx)
        {
            return ctx.Items.TryGet("BTCPAY.APPDATA") as AppData;
        }

        public static void SetAppData(this HttpContext ctx, AppData appData)
        {
            ctx.Items["BTCPAY.APPDATA"] = appData;
        }

        public static bool SupportChain(this IConfiguration conf, string cryptoCode)
        {
            var supportedChains = conf.GetOrDefault<string>("chains", "btc")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.ToUpperInvariant()).ToHashSet();
            return supportedChains.Contains(cryptoCode.ToUpperInvariant());
        }

        public static IActionResult RedirectToRecoverySeedBackup(this Controller controller, RecoverySeedBackupViewModel vm)
        {
            var redirectVm = new PostRedirectViewModel
            {
                AspController = "UIHome",
                AspAction = "RecoverySeedBackup",
                FormParameters =
                {
                    { "cryptoCode", vm.CryptoCode },
                    { "mnemonic", vm.Mnemonic },
                    { "passphrase", vm.Passphrase },
                    { "isStored", vm.IsStored ? "true" : "false" },
                    { "requireConfirm", vm.RequireConfirm ? "true" : "false" },
                    { "returnUrl", vm.ReturnUrl }
                }
            };
            return controller.View("PostRedirect", redirectVm);
        }

        public static string ToSql<TEntity>(this IQueryable<TEntity> query) where TEntity : class
        {
            var enumerator = query.Provider.Execute<IEnumerable<TEntity>>(query.Expression).GetEnumerator();
            var relationalCommandCache = enumerator.Private("_relationalCommandCache");
            var selectExpression = relationalCommandCache.Private<Microsoft.EntityFrameworkCore.Query.SqlExpressions.SelectExpression>("_selectExpression");
            var factory = relationalCommandCache.Private<Microsoft.EntityFrameworkCore.Query.IQuerySqlGeneratorFactory>("_querySqlGeneratorFactory");

            var sqlGenerator = factory.Create();
            var command = sqlGenerator.GetCommand(selectExpression);

            string sql = command.CommandText;
            return sql;
        }

        public static BTCPayNetworkProvider ConfigureNetworkProvider(this IConfiguration configuration, Logs logs)
        {
            var _networkType = DefaultConfiguration.GetNetworkType(configuration);
            var supportedChains = configuration.GetOrDefault<string>("chains", "btc")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.ToUpperInvariant()).ToHashSet();
            foreach (var c in supportedChains.ToList())
            {
                if (new[] { "ETH", "USDT20", "FAU" }.Contains(c, StringComparer.OrdinalIgnoreCase))
                {
                    logs.Configuration.LogWarning($"'{c}' is not anymore supported, please remove it from 'chains'");
                    supportedChains.Remove(c);
                }
            }
            var networkProvider = new BTCPayNetworkProvider(_networkType);
            var filtered = networkProvider.Filter(supportedChains.ToArray());
#if ALTCOINS
            supportedChains.AddRange(filtered.GetAllElementsSubChains(networkProvider));
#endif
#if !ALTCOINS
            var onlyBTC = supportedChains.Count == 1 && supportedChains.First() == "BTC";
            if (!onlyBTC)
                throw new ConfigException($"This build of BTCPay Server does not support altcoins");
#endif
            var result = networkProvider.Filter(supportedChains.ToArray());
            foreach (var chain in supportedChains)
            {
                if (result.GetNetwork<BTCPayNetworkBase>(chain) == null)
                    throw new ConfigException($"Invalid chains \"{chain}\"");
            }

            logs.Configuration.LogInformation(
                "Supported chains: " + String.Join(',', supportedChains.ToArray()));
            return result;
        }

        public static DataDirectories Configure(this DataDirectories dataDirectories, IConfiguration configuration)
        {
            var networkType = DefaultConfiguration.GetNetworkType(configuration);
            var defaultSettings = BTCPayDefaultSettings.GetDefaultSettings(networkType);
            dataDirectories.DataDir = configuration["datadir"] ?? defaultSettings.DefaultDataDirectory;
            dataDirectories.PluginDir = configuration["plugindir"] ?? defaultSettings.DefaultPluginDirectory;
            dataDirectories.StorageDir = Path.Combine(dataDirectories.DataDir, Storage.Services.Providers.FileSystemStorage.FileSystemFileProviderService.LocalStorageDirectoryName);
            dataDirectories.TempStorageDir = Path.Combine(dataDirectories.StorageDir, "tmp");
            dataDirectories.TempDir = Path.Combine(dataDirectories.DataDir, "tmp");
            return dataDirectories;
        }

        private static object Private(this object obj, string privateField) => obj?.GetType().GetField(privateField, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(obj);
        private static T Private<T>(this object obj, string privateField) => (T)obj?.GetType().GetField(privateField, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(obj);
    }
}
