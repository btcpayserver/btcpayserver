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
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;
using BTCPayServer.BIP78.Sender;
using NBitcoin.Payment;
using NBitpayClient;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer
{
    public static class Extensions
    {

        public static bool TryGetPayjoinEndpoint(this BitcoinUrlBuilder bip21, out Uri endpoint)
        {
            endpoint = bip21.UnknowParameters.TryGetValue($"{PayjoinClient.BIP21EndpointKey}", out var uri) ? new Uri(uri, UriKind.Absolute) : null;
            return endpoint != null;
        }

        public static bool IsValidFileName(this string fileName)
        {
            return !fileName.ToCharArray().Any(c => Path.GetInvalidFileNameChars().Contains(c)
            || c == Path.AltDirectorySeparatorChar
            || c == Path.DirectorySeparatorChar
            || c == Path.PathSeparator
            || c == '\\');
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

        public static string Truncate(this string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        public static string PrettyPrint(this TimeSpan expiration)
        {
            StringBuilder builder = new StringBuilder();
            if (expiration.Days >= 1)
                builder.Append(expiration.Days.ToString(CultureInfo.InvariantCulture));
            if (expiration.Hours >= 1)
                builder.Append(expiration.Hours.ToString("00", CultureInfo.InvariantCulture));
            builder.Append($"{expiration.Minutes.ToString("00", CultureInfo.InvariantCulture)}:{expiration.Seconds.ToString("00", CultureInfo.InvariantCulture)}");
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

        public static bool HasStatusMessage(this ITempDataDictionary tempData)
        {
            return (tempData.Peek(WellKnownTempData.SuccessMessage) ??
                   tempData.Peek(WellKnownTempData.ErrorMessage) ??
                   tempData.Peek("StatusMessageModel")) != null;
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
                    using (CancellationTokenSource cts = new CancellationTokenSource())
                    {
                        cts.CancelAfter(5000);
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cts.Token);
                    }
                }
            }
            catch { }
            finally { try { webSocket.Dispose(); } catch { } }
        }

        public static IEnumerable<BitcoinLikePaymentData> GetAllBitcoinPaymentData(this InvoiceEntity invoice)
        {
            return invoice.GetPayments()
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

        public static string WithTrailingSlash(this string str)
        {
            if (str.EndsWith("/", StringComparison.InvariantCulture))
                return str;
            return str + "/";
        }
        public static string WithStartingSlash(this string str)
        {
            if (str.StartsWith("/", StringComparison.InvariantCulture))
                return str;
            return $"/{str}";
        }
        public static string WithoutEndingSlash(this string str)
        {
            if (str.EndsWith("/", StringComparison.InvariantCulture))
                return str.Substring(0, str.Length - 1);
            return str;
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

        public static bool IsSegwit(this DerivationStrategyBase derivationStrategyBase)
        {
            return ScriptPubKeyType(derivationStrategyBase) != NBitcoin.ScriptPubKeyType.Legacy;
        }
        public static ScriptPubKeyType ScriptPubKeyType(this DerivationStrategyBase derivationStrategyBase)
        {
            if (IsSegwitCore(derivationStrategyBase))
            {
                return NBitcoin.ScriptPubKeyType.Segwit;
            }

            return (derivationStrategyBase is P2SHDerivationStrategy p2shStrat && IsSegwitCore(p2shStrat.Inner))
                ? NBitcoin.ScriptPubKeyType.SegwitP2SH
                : NBitcoin.ScriptPubKeyType.Legacy;
        }
        private static bool IsSegwitCore(DerivationStrategyBase derivationStrategyBase)
        {
            return (derivationStrategyBase is P2WSHDerivationStrategy) ||
                            (derivationStrategyBase is DirectDerivationStrategy direct) && direct.Segwit;
        }

        public static bool IsLocalNetwork(string server)
        {
            if (server == null)
                throw new ArgumentNullException(nameof(server));
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

        

        public static StatusMessageModel GetStatusMessageModel(this ITempDataDictionary tempData)
        {
            tempData.TryGetValue(WellKnownTempData.SuccessMessage, out var successMessage);
            tempData.TryGetValue(WellKnownTempData.ErrorMessage, out var errorMessage);
            tempData.TryGetValue("StatusMessageModel", out var model);
            if (successMessage != null || errorMessage != null)
            {
                var parsedModel = new StatusMessageModel();
                parsedModel.Message = (string)successMessage ?? (string)errorMessage;
                if (successMessage != null)
                {
                    parsedModel.Severity = StatusMessageModel.StatusSeverity.Success;
                }
                else
                {
                    parsedModel.Severity = StatusMessageModel.StatusSeverity.Error;
                }
                return parsedModel;
            }
            else if (model != null && model is string str)
            {
                return JObject.Parse(str).ToObject<StatusMessageModel>();
            }
            return null;
        }

        public static bool IsOnion(this HttpRequest request)
        {
            if (request?.Host.Host == null)
                return false;
            return request.Host.Host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsOnion(this Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri)
                return false;
            return uri.DnsSafeHost.EndsWith(".onion", StringComparison.OrdinalIgnoreCase);
        }


        public static string GetAbsoluteRoot(this HttpRequest request)
        {
            return string.Concat(
                        request.Scheme,
                        "://",
                        request.Host.ToUriComponent(),
                        request.PathBase.ToUriComponent());
        }

        public static Uri GetAbsoluteRootUri(this HttpRequest request)
        {
            return new Uri(request.GetAbsoluteRoot());
        }

        public static string GetCurrentUrl(this HttpRequest request)
        {
            return string.Concat(
                        request.Scheme,
                        "://",
                        request.Host.ToUriComponent(),
                        request.PathBase.ToUriComponent(),
                        request.Path.ToUriComponent());
        }

        public static string GetCurrentPath(this HttpRequest request)
        {
            return string.Concat(
                        request.PathBase.ToUriComponent(),
                        request.Path.ToUriComponent());
        }
        public static string GetCurrentPathWithQueryString(this HttpRequest request)
        {
            return request.PathBase + request.Path + request.QueryString;
        }

        /// <summary>
        /// If 'toto' and RootPath is 'rootpath' returns '/rootpath/toto'
        /// If 'toto' and RootPath is empty returns '/toto'
        /// </summary>
        /// <param name="request"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string GetRelativePath(this HttpRequest request, string path)
        {
            if (path.Length > 0 && path[0] != '/')
                path = $"/{path}";
            return string.Concat(
                        request.PathBase.ToUriComponent(),
                        path);
        }

        /// <summary>
        /// If 'https://example.com/toto' returns 'https://example.com/toto'
        /// If 'toto' and RootPath is 'rootpath' returns '/rootpath/toto'
        /// If 'toto' and RootPath is empty returns '/toto'
        /// </summary>
        /// <param name="request"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string GetRelativePathOrAbsolute(this HttpRequest request, string path)
        {
            if (!Uri.TryCreate(path, UriKind.RelativeOrAbsolute, out var uri) ||
                uri.IsAbsoluteUri)
                return path;

            if (path.Length > 0 && path[0] != '/')
                path = $"/{path}";
            return string.Concat(
                        request.PathBase.ToUriComponent(),
                        path);
        }

        public static string GetAbsoluteUri(this HttpRequest request, string redirectUrl)
        {
            bool isRelative =
                (redirectUrl.Length > 0 && redirectUrl[0] == '/')
                || !new Uri(redirectUrl, UriKind.RelativeOrAbsolute).IsAbsoluteUri;
            return isRelative ? request.GetAbsoluteRoot() + redirectUrl : redirectUrl;
        }

        /// <summary>
        /// Will return an absolute URL. 
        /// If `relativeOrAsbolute` is absolute, returns it.
        /// If `relativeOrAsbolute` is relative, send absolute url based on the HOST of this request (without PathBase)
        /// </summary>
        /// <param name="request"></param>
        /// <param name="relativeOrAbsolte"></param>
        /// <returns></returns>
        public static Uri GetAbsoluteUriNoPathBase(this HttpRequest request, Uri relativeOrAbsolute = null)
        {
            if (relativeOrAbsolute == null)
            {
                return new Uri(string.Concat(
                    request.Scheme,
                    "://",
                    request.Host.ToUriComponent()), UriKind.Absolute);
            }
            if (relativeOrAbsolute.IsAbsoluteUri)
                return relativeOrAbsolute;
            return new Uri(string.Concat(
                    request.Scheme,
                    "://",
                    request.Host.ToUriComponent()) + relativeOrAbsolute.ToString().WithStartingSlash(), UriKind.Absolute);
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

        public static StoreData GetStoreData(this HttpContext ctx)
        {
            return ctx.Items.TryGet("BTCPAY.STOREDATA") as StoreData;
        }
        public static void SetStoreData(this HttpContext ctx, StoreData storeData)
        {
            ctx.Items["BTCPAY.STOREDATA"] = storeData;
        }

        public static StoreData[] GetStoresData(this HttpContext ctx)
        {
            return ctx.Items.TryGet("BTCPAY.STORESDATA") as StoreData[];
        }
        public static void SetStoresData(this HttpContext ctx, StoreData[] storeData)
        {
            ctx.Items["BTCPAY.STORESDATA"] = storeData;
        }

        public static IActionResult RedirectToRecoverySeedBackup(this Controller controller, RecoverySeedBackupViewModel vm)
        {
            var redirectVm = new PostRedirectViewModel()
            {
                AspController = "Home",
                AspAction = "RecoverySeedBackup",
                Parameters =
                {
                    new KeyValuePair<string, string>("cryptoCode", vm.CryptoCode),
                    new KeyValuePair<string, string>("mnemonic", vm.Mnemonic),
                    new KeyValuePair<string, string>("passphrase", vm.Passphrase),
                    new KeyValuePair<string, string>("isStored", vm.IsStored ? "true" : "false"),
                    new KeyValuePair<string, string>("requireConfirm", vm.RequireConfirm ? "true" : "false"),
                    new KeyValuePair<string, string>("returnUrl", vm.ReturnUrl)
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

        public static BTCPayNetworkProvider ConfigureNetworkProvider(this IConfiguration configuration)
        {
            var _networkType = DefaultConfiguration.GetNetworkType(configuration);
            var supportedChains = configuration.GetOrDefault<string>("chains", "btc")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.ToUpperInvariant()).ToHashSet();

            var networkProvider = new BTCPayNetworkProvider(_networkType);
            var filtered = networkProvider.Filter(supportedChains.ToArray());
#if ALTCOINS
            supportedChains.AddRange(filtered.GetAllElementsSubChains(networkProvider));
            supportedChains.AddRange(filtered.GetAllEthereumSubChains(networkProvider));
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

            Logs.Configuration.LogInformation(
                "Supported chains: " + String.Join(',', supportedChains.ToArray()));
            return result;
        }

        public static DataDirectories Configure(this DataDirectories dataDirectories, IConfiguration configuration)
        {
            var networkType = DefaultConfiguration.GetNetworkType(configuration);
            var defaultSettings = BTCPayDefaultSettings.GetDefaultSettings(networkType);
            dataDirectories.DataDir = configuration["datadir"] ?? defaultSettings.DefaultDataDirectory;
            dataDirectories.PluginDir = configuration["plugindir"] ?? defaultSettings.DefaultPluginDirectory;
            dataDirectories.StorageDir = Path.Combine(dataDirectories.DataDir , Storage.Services.Providers.FileSystemStorage.FileSystemFileProviderService.LocalStorageDirectoryName);
            dataDirectories.TempStorageDir = Path.Combine(dataDirectories.StorageDir, "tmp");
            return dataDirectories;
        }

        private static object Private(this object obj, string privateField) => obj?.GetType().GetField(privateField, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(obj);
        private static T Private<T>(this object obj, string privateField) => (T)obj?.GetType().GetField(privateField, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(obj);
    }
}
