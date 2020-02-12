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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Newtonsoft.Json.Linq;

namespace BTCPayServer
{
    public static class Extensions
    {
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
        public static IServiceCollection AddStartupTask<T>(this IServiceCollection services)
            where T : class, IStartupTask
            => services.AddTransient<IStartupTask, T>();
        public static async Task StartWithTasksAsync(this IWebHost webHost, CancellationToken cancellationToken = default)
        {
            // Load all tasks from DI
            var startupTasks = webHost.Services.GetServices<IStartupTask>();

            // Execute all the tasks
            foreach (var startupTask in startupTasks)
            {
                await startupTask.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }

            // Start the tasks as normal
            await webHost.StartAsync(cancellationToken).ConfigureAwait(false);
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
                return ip.IsLocal() || ip.IsRFC1918();
            }
            return false;
        }

        public static void SetStatusMessageModel(this ITempDataDictionary tempData, StatusMessageModel statusMessage)
        {
            if (statusMessage == null)
            {
                tempData.Remove("StatusMessageModel");
                return;
            }
            tempData["StatusMessageModel"] = JObject.FromObject(statusMessage).ToString(Formatting.None);
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
