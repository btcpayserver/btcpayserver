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

namespace BTCPayServer
{
    public static class Extensions
    {
        public static string Prettify(this TimeSpan timeSpan)
        {
            if (timeSpan.TotalMinutes < 1)
            {
                return $"{(int)timeSpan.TotalSeconds} second{Plural((int)timeSpan.TotalSeconds)}";
            }
            if (timeSpan.TotalHours < 1)
            {
                return $"{(int)timeSpan.TotalMinutes} minute{Plural((int)timeSpan.TotalMinutes)}";
            }
            if (timeSpan.Days < 1)
            {
                return $"{(int)timeSpan.TotalHours} hour{Plural((int)timeSpan.TotalHours)}";
            }
            return $"{(int)timeSpan.TotalDays} day{Plural((int)timeSpan.TotalDays)}";
        }

        private static string Plural(int totalDays)
        {
            return totalDays > 1 ? "s" : string.Empty;
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

        public static bool IsCoinAverage(this string exchangeName)
        {
            string[] coinAverages = new[] { "coinaverage", "bitcoinaverage" };
            return String.IsNullOrWhiteSpace(exchangeName) ? true : coinAverages.Contains(exchangeName, StringComparer.OrdinalIgnoreCase) ? true : false;
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

        public static string GetAbsoluteRoot(this HttpRequest request)
        {
            return string.Concat(
                        request.Scheme,
                        "://",
                        request.Host.ToUriComponent(),
                        request.PathBase.ToUriComponent());
        }

        public static string GetAbsoluteUri(this HttpRequest request, string redirectUrl)
        {
            bool isRelative = 
                (redirectUrl.Length > 0 && redirectUrl[0] == '/')
                || !new Uri(redirectUrl, UriKind.RelativeOrAbsolute).IsAbsoluteUri;
            return isRelative ? request.GetAbsoluteRoot() + redirectUrl : redirectUrl;
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

        public static bool GetIsBitpayAPI(this HttpContext ctx)
        {
            return ctx.Items.TryGetValue("IsBitpayAPI", out object obj) &&
                  obj is bool b && b;
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

        public static HtmlString ToJSVariableModel(this object o, string variableName)
        {
            var encodedJson = JavaScriptEncoder.Default.Encode(o.ToJson());
            return new HtmlString($"var {variableName} = JSON.parse('" + encodedJson + "');");
        }


    }
}
