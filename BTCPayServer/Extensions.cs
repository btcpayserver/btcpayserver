using BTCPayServer.Authentication;
using BTCPayServer.Configuration;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using NBitcoin;
using System.Threading.Tasks;
using NBXplorer;
using NBXplorer.Models;
using System.Linq;
using System.Threading;
using BTCPayServer.Services.Wallets;
using System.IO;
using BTCPayServer.Logging;
using Microsoft.Extensions.Logging;
using System.Net.WebSockets;
using BTCPayServer.Services.Invoices;
using NBitpayClient;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Identity;
using BTCPayServer.Models;
using System.Security.Claims;
using System.Globalization;
using BTCPayServer.Services;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NBXplorer.DerivationStrategy;
using System.Net;

namespace BTCPayServer
{
    public static class Extensions
    {
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
        public static decimal RoundToSignificant(this decimal value, ref int divisibility)
        {
            if (value != 0m)
            {
                while (true)
                {
                    var rounded = decimal.Round(value, divisibility, MidpointRounding.AwayFromZero);
                    if ((Math.Abs(rounded - value) / value) < 0.001m)
                    {
                        value = rounded;
                        break;
                    }
                    divisibility++;
                }
            }
            return value;
        }
        public static PaymentMethodId GetpaymentMethodId(this InvoiceCryptoInfo info)
        {
            return new PaymentMethodId(info.CryptoCode, Enum.Parse<PaymentTypes>(info.PaymentType));
        }
        public static async Task CloseSocket(this WebSocket webSocket)
        {
            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    CancellationTokenSource cts = new CancellationTokenSource();
                    cts.CancelAfter(5000);
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cts.Token);
                }
            }
            catch { }
            finally { try { webSocket.Dispose(); } catch { } }
        }
        public static bool SupportDropColumn(this Microsoft.EntityFrameworkCore.Migrations.Migration migration, string activeProvider)
        {
            return activeProvider != "Microsoft.EntityFrameworkCore.Sqlite";
        }

        public static bool SupportDropForeignKey(this Microsoft.EntityFrameworkCore.Migrations.Migration migration, string activeProvider)
        {
            return activeProvider != "Microsoft.EntityFrameworkCore.Sqlite";
        }
        public static bool SupportDropForeignKey(this DatabaseFacade facade)
        {
            return facade.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite";
        }

        public static async Task<Dictionary<uint256, TransactionResult>> GetTransactions(this BTCPayWallet client, uint256[] hashes, CancellationToken cts = default(CancellationToken))
        {
            hashes = hashes.Distinct().ToArray();
            var transactions = hashes
                                        .Select(async o => await client.GetTransactionAsync(o, cts))
                                        .ToArray();
            await Task.WhenAll(transactions).ConfigureAwait(false);
            return transactions.Select(t => t.Result).Where(t => t != null).ToDictionary(o => o.Transaction.GetHash());
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
            if (IsSegwitCore(derivationStrategyBase))
                return true;
            return (derivationStrategyBase is P2SHDerivationStrategy p2shStrat && IsSegwitCore(p2shStrat.Inner));
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
            if(IPAddress.TryParse(server, out var ip))
            {
                return ip.IsLocal();
            }
            return false;
        }

        public static bool IsOnion(this HttpRequest request)
        {
            if (request?.Host.Host == null)
                return false;
            return request.Host.Host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase);
        }

        public static string GetAbsoluteRoot(this HttpRequest request)
        {
            return string.Concat(
                        request.Scheme,
                        "://",
                        request.Host.ToUriComponent(),
                        request.PathBase.ToUriComponent());
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

        public static IServiceCollection ConfigureBTCPayServer(this IServiceCollection services, IConfiguration conf)
        {
            services.Configure<BTCPayServerOptions>(o =>
            {
                o.LoadArgs(conf);
            });
            return services;
        }

        public static string GetSIN(this ClaimsPrincipal principal)
        {
            return principal.Claims.Where(c => c.Type == Claims.SIN).Select(c => c.Value).FirstOrDefault();
        }

        public static string GetStoreId(this ClaimsPrincipal principal)
        {
            return principal.Claims.Where(c => c.Type == Claims.OwnStore).Select(c => c.Value).FirstOrDefault();
        }

        public static void SetIsBitpayAPI(this HttpContext ctx, bool value)
        {
            NBitcoin.Extensions.TryAdd(ctx.Items, "IsBitpayAPI", value);
        }

        public static void AddRange<T>(this HashSet<T> hashSet, IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                hashSet.Add(item);
            }
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

        public static (string Signature, String Id, String Authorization) GetBitpayAuth(this HttpContext ctx)
        {
            ctx.Items.TryGetValue("BitpayAuth", out object obj);
            return ((string Signature, String Id, String Authorization))obj;
        }

        public static StoreData GetStoreData(this HttpContext ctx)
        {
            return ctx.Items.TryGet("BTCPAY.STOREDATA") as StoreData;
        }
        public static void SetStoreData(this HttpContext ctx, StoreData storeData)
        {
            ctx.Items["BTCPAY.STOREDATA"] = storeData;
        }

        private static JsonSerializerSettings jsonSettings = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        public static string ToJson(this object o)
        {
            var res = JsonConvert.SerializeObject(o, Formatting.None, jsonSettings);
            return res;
        }
        
        public static string TrimEnd(this string input, string suffixToRemove,
            StringComparison comparisonType) {

            if (input != null && suffixToRemove != null
                              && input.EndsWith(suffixToRemove, comparisonType)) {
                return input.Substring(0, input.Length - suffixToRemove.Length);
            }
            else return input;
        }
    }
}
