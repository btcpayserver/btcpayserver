
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.BIP78.Sender;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.NTag424;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Payouts;
using BTCPayServer.Security;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Reporting;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBitcoin.Payment;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using InvoiceCryptoInfo = BTCPayServer.Services.Invoices.InvoiceCryptoInfo;

namespace BTCPayServer
{
    public static class Extensions
    {
        /// <summary>
        /// Outputs a serializer which will serialize default and null members.
        /// This is useful for discovering the API.
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static JsonSerializer ForAPI(this JsonSerializer settings)
        {
            var clone = new JsonSerializer()
            {
                CheckAdditionalContent = settings.CheckAdditionalContent,
                ConstructorHandling = settings.ConstructorHandling,
                ContractResolver = settings.ContractResolver,
                Culture = settings.Culture,
                DateFormatHandling = settings.DateFormatHandling,
                DateFormatString = settings.DateFormatString,
                DateParseHandling = settings.DateParseHandling,
                DateTimeZoneHandling = settings.DateTimeZoneHandling,
                DefaultValueHandling = settings.DefaultValueHandling,
                EqualityComparer = settings.EqualityComparer,
                FloatFormatHandling = settings.FloatFormatHandling,
                FloatParseHandling = settings.FloatParseHandling,
                Formatting = settings.Formatting,
                MaxDepth = settings.MaxDepth,
                MetadataPropertyHandling = settings.MetadataPropertyHandling,
                Context = settings.Context,
                MissingMemberHandling = settings.MissingMemberHandling,
                NullValueHandling = settings.NullValueHandling,
                ObjectCreationHandling = settings.ObjectCreationHandling,
                PreserveReferencesHandling = settings.PreserveReferencesHandling,
                ReferenceLoopHandling = settings.ReferenceLoopHandling,
                StringEscapeHandling = settings.StringEscapeHandling,
                TraceWriter = settings.TraceWriter,
                TypeNameAssemblyFormatHandling = settings.TypeNameAssemblyFormatHandling,
                SerializationBinder = settings.SerializationBinder,
                TypeNameHandling = settings.TypeNameHandling,
                ReferenceResolver = settings.ReferenceResolver
            };
            foreach (var conv in settings.Converters)
                clone.Converters.Add(conv);
            clone.NullValueHandling = NullValueHandling.Include;
            clone.DefaultValueHandling = DefaultValueHandling.Include;
            return clone;
        }
        public static DerivationSchemeParser GetDerivationSchemeParser(this BTCPayNetwork network)
        {
            return new DerivationSchemeParser(network);
        }

        public static bool TryParseXpub(this DerivationSchemeParser derivationSchemeParser, string xpub,
            ref DerivationSchemeSettings derivationSchemeSettings, bool electrum = false)
        {
            if (!electrum)
            {
                var isOD = Regex.Match(xpub, @"\(.*?\)").Success;
                try
                {
                    var result = derivationSchemeParser.ParseOutputDescriptor(xpub);
                    derivationSchemeSettings.AccountOriginal = xpub.Trim();
                    derivationSchemeSettings.AccountDerivation = result.Item1;
                    derivationSchemeSettings.AccountKeySettings = result.Item2.Select((path, i) => new AccountKeySettings()
                    {
                        RootFingerprint = path?.MasterFingerprint,
                        AccountKeyPath = path?.KeyPath,
                        AccountKey = result.Item1.GetExtPubKeys().ElementAt(i).GetWif(derivationSchemeParser.Network)
                    }).ToArray();
                    return true;
                }
                catch (Exception)
                {
                    if (isOD)
                    {
                        return false;
                    } // otherwise continue and try to parse input as xpub
                }
            }
            try
            {
                // Extract fingerprint and account key path from export formats that contain them.
                // Possible formats: [fingerprint/account_key_path]xpub, [fingerprint]xpub, xpub
                HDFingerprint? rootFingerprint = null;
                KeyPath accountKeyPath = null;
                var derivationRegex = new Regex(@"^(?:\[(\w+)(?:\/(.*?))?\])?(\w+)$", RegexOptions.IgnoreCase);
                var match = derivationRegex.Match(xpub.Trim());
                if (match.Success)
                {
                    if (!string.IsNullOrEmpty(match.Groups[1].Value))
                        rootFingerprint = HDFingerprint.Parse(match.Groups[1].Value);
                    if (!string.IsNullOrEmpty(match.Groups[2].Value))
                        accountKeyPath = KeyPath.Parse(match.Groups[2].Value);
                    if (!string.IsNullOrEmpty(match.Groups[3].Value))
                        xpub = match.Groups[3].Value;
                }
                derivationSchemeSettings.AccountOriginal = xpub.Trim();
                derivationSchemeSettings.AccountDerivation = electrum ? derivationSchemeParser.ParseElectrum(derivationSchemeSettings.AccountOriginal) : derivationSchemeParser.Parse(derivationSchemeSettings.AccountOriginal);
                derivationSchemeSettings.AccountKeySettings = derivationSchemeSettings.AccountDerivation.GetExtPubKeys()
                    .Select(key => new AccountKeySettings
                    {
                        AccountKey = key.GetWif(derivationSchemeParser.Network)
                    }).ToArray();
                if (derivationSchemeSettings.AccountDerivation is DirectDerivationStrategy direct && !direct.Segwit)
                    derivationSchemeSettings.AccountOriginal = null; // Saving this would be confusing for user, as xpub of electrum is legacy derivation, but for btcpay, it is segwit derivation
                // apply initial matches if there were no results from parsing
                if (rootFingerprint != null && derivationSchemeSettings.AccountKeySettings[0].RootFingerprint == null)
                {
                    derivationSchemeSettings.AccountKeySettings[0].RootFingerprint = rootFingerprint;
                }
                if (accountKeyPath != null && derivationSchemeSettings.AccountKeySettings[0].AccountKeyPath == null)
                {
                    derivationSchemeSettings.AccountKeySettings[0].AccountKeyPath = accountKeyPath;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static CardKey CreatePullPaymentCardKey(this IssuerKey issuerKey, byte[] uid, int version, string pullPaymentId)
        {
            var data = Encoding.UTF8.GetBytes(pullPaymentId);
            return issuerKey.CreateCardKey(uid, version, data);
        }
        public static DateTimeOffset TruncateMilliSeconds(this DateTimeOffset dt) => new (dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, 0, dt.Offset);
        public static decimal? GetDue(this InvoiceCryptoInfo invoiceCryptoInfo)
        {
            if (invoiceCryptoInfo is null)
                return null;
            if (decimal.TryParse(invoiceCryptoInfo.Due, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                return v;
            return null;
        }
        public static Task<BufferizedFormFile> Bufferize(this IFormFile formFile)
        {
            return BufferizedFormFile.Bufferize(formFile);
        }
        /// <summary>
        /// Unescape Uri string for %2F
        /// See details at: https://github.com/dotnet/aspnetcore/issues/14170#issuecomment-533342396
        /// </summary>
        /// <param name="uriString">The Uri string.</param>
        /// <returns>Unescaped back slash Uri string.</returns>
        public static string UnescapeBackSlashUriString(string uriString)
        {
            if (uriString == null)
            {
                return null;
            }
            return uriString.Replace("%2f", "%2F").Replace("%2F", "/");
        }
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

        public static Uri GetServerUri(this ILightningClient client)
        {
            var kv = LightningConnectionStringHelper.ExtractValues(client.ToString(), out _);
            
            return !kv.TryGetValue("server", out var server) ? null : new Uri(server, UriKind.Absolute);
        }

        public static string GetDisplayName(this ILightningClient client)
        {
            LightningConnectionStringHelper.ExtractValues(client.ToString(), out var type);

            var lncType = typeof(LightningConnectionType);
            var fields = lncType.GetFields(BindingFlags.Public | BindingFlags.Static);
            var field = fields.FirstOrDefault(f => f.GetValue(lncType)?.ToString() == type);
            if (field == null) return type;
            DisplayAttribute attr = field.GetCustomAttribute<DisplayAttribute>();
            return attr?.Name ?? type;
        }

        public static bool IsSafe(this ILightningClient client)
        {
            var kv = LightningConnectionStringHelper.ExtractValues(client.ToString(), out _);
            if (kv.TryGetValue("cookiefilepath", out _)  ||
                kv.TryGetValue("macaroondirectorypath", out _)  ||
                kv.TryGetValue("macaroonfilepath", out _) )
                return false;

            if (!kv.TryGetValue("server", out var server))
            {
                return true;
            }
            var uri = new Uri(server, UriKind.Absolute);
            if (uri.Scheme.Equals("unix", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!Utils.TryParseEndpoint(uri.DnsSafeHost, 80, out _))
                return false;
            return !IsLocalNetwork(uri.DnsSafeHost);
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
            try
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
            catch (OverflowException)
            {
                return value;
            }
        }


        public static IServiceCollection AddUIExtension(this IServiceCollection services, string location, string partialViewName)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            services.AddSingleton<IUIExtension>(new UIExtension(partialViewName, location));
#pragma warning restore CS0618 // Type or member is obsolete
            return services;
        }
        public static IServiceCollection AddReportProvider<T>(this IServiceCollection services)
    where T : ReportProvider
        {
            services.AddSingleton<T>();
            services.AddSingleton<ReportProvider, T>();
            return services;
        }

        public static IServiceCollection AddScheduledTask<T>(this IServiceCollection services, TimeSpan every)
            where T : class, IPeriodicTask
        {
            services.AddSingleton<T>();
            services.AddTransient<ScheduledTask>(o => new ScheduledTask(typeof(T), every));
            return services;
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

        public static IEnumerable<BitcoinLikePaymentData> GetAllBitcoinPaymentData(this InvoiceEntity invoice, BitcoinLikePaymentHandler handler, bool accountedOnly)
        {
            return invoice.GetPayments(accountedOnly)
                .Select(p => p.GetDetails<BitcoinLikePaymentData>(handler))
                .Where(p => p is not null);
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
#nullable enable
        public static LNURLPayPaymentHandler GetLNURLHandler(this PaymentMethodHandlerDictionary handlers, BTCPayNetwork network)
        {
            return handlers.GetLNURLHandler(network.CryptoCode);
        }
        public static LNURLPayPaymentHandler GetLNURLHandler(this PaymentMethodHandlerDictionary handlers, string cryptoCode)
        {
            var pmi = PaymentTypes.LNURL.GetPaymentMethodId(cryptoCode);
            var h = (LNURLPayPaymentHandler)handlers[pmi];
            return h;
        }
        public static LightningLikePaymentHandler GetLightningHandler(this PaymentMethodHandlerDictionary handlers, BTCPayNetwork network)
        {
            return handlers.GetLightningHandler(network.CryptoCode);
        }
        public static LightningLikePaymentHandler GetLightningHandler(this PaymentMethodHandlerDictionary handlers, string cryptoCode)
        {
            var pmi = PaymentTypes.LN.GetPaymentMethodId(cryptoCode);
            var h = (LightningLikePaymentHandler)handlers[pmi];
            return h;
        }

        public static BitcoinLikePaymentHandler? TryGetBitcoinHandler(this PaymentMethodHandlerDictionary handlers, BTCPayNetwork network)
        => handlers.TryGetBitcoinHandler(network.CryptoCode);
        public static BitcoinLikePaymentHandler? TryGetBitcoinHandler(this PaymentMethodHandlerDictionary handlers, string cryptoCode)
         => handlers.TryGetBitcoinHandler(PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode));
        public static BitcoinLikePaymentHandler? TryGetBitcoinHandler(this PaymentMethodHandlerDictionary handlers, PaymentMethodId paymentMethodId)
        {
            if (handlers.TryGetValue(paymentMethodId, out var h) && h is BitcoinLikePaymentHandler b)
                return b;
            return null;
        }
        public static BitcoinLikePaymentHandler GetBitcoinHandler(this PaymentMethodHandlerDictionary handlers, BTCPayNetwork network)
        => handlers.GetBitcoinHandler(network.CryptoCode);
        public static BitcoinLikePaymentHandler GetBitcoinHandler(this PaymentMethodHandlerDictionary handlers, string cryptoCode)
        {
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            var h = (BitcoinLikePaymentHandler)handlers[pmi];
            return h;
        }
        public static BTCPayNetwork? TryGetNetwork<TId, THandler>(this HandlersDictionary<TId, THandler> handlers, TId id) 
                                                                           where THandler : IHandler<TId>
                                                                           where TId : notnull
        {
            if (id is not null &&
                handlers.TryGetValue(id, out var value) &&
                value is IHasNetwork { Network: var n })
            {
                return n;
            }
            return null;
        }
        public static BTCPayNetwork GetNetwork<TId, THandler>(this HandlersDictionary<TId, THandler> handlers, TId id)
                                                                           where THandler : IHandler<TId>
                                                                           where TId : notnull
        {
            return TryGetNetwork(handlers, id) ?? throw new KeyNotFoundException($"Network for {id} is not found");
        }
        public static LightningPaymentMethodConfig? GetLightningConfig(this PaymentMethodHandlerDictionary handlers, Data.StoreData store, BTCPayNetwork network)
        {
            var config = store.GetPaymentMethodConfig(PaymentTypes.LN.GetPaymentMethodId(network.CryptoCode));
            if (config is null)
                return null;
            return handlers.GetLightningHandler(network).ParsePaymentMethodConfig(config);
        }
        public static DerivationStrategyBase? GetDerivationStrategy(this PaymentMethodHandlerDictionary handlers, InvoiceEntity invoice, BTCPayNetworkBase network)
        {
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            if (!handlers.TryGetValue(pmi, out var handler))
                return null;
            var prompt = invoice.GetPaymentPrompt(pmi);
            if (prompt?.Details is null)
                return null;
            var details = (BitcoinPaymentPromptDetails)handler.ParsePaymentPromptDetails(prompt.Details);
            return details.AccountDerivation;
        }
#nullable restore
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

        public static DataDirectories Configure(this DataDirectories dataDirectories, IConfiguration configuration)
        {
            var networkType = DefaultConfiguration.GetNetworkType(configuration);
            var defaultSettings = BTCPayDefaultSettings.GetDefaultSettings(networkType);
            dataDirectories.DataDir = configuration["datadir"] ?? defaultSettings.DefaultDataDirectory;
            dataDirectories.PluginDir = configuration["plugindir"] ?? defaultSettings.DefaultPluginDirectory;
            dataDirectories.StorageDir = Path.Combine(dataDirectories.DataDir, Storage.Services.Providers.FileSystemStorage.FileSystemFileProviderService.LocalStorageDirectoryName);
            dataDirectories.TempStorageDir = Path.Combine(dataDirectories.StorageDir, "tmp");
            dataDirectories.TempDir = Path.Combine(dataDirectories.DataDir, "tmp");
            dataDirectories.LangsDir = Path.Combine(dataDirectories.DataDir, "Langs");
            return dataDirectories;
        }

        private static object Private(this object obj, string privateField) => obj?.GetType().GetField(privateField, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(obj);
        private static T Private<T>(this object obj, string privateField) => (T)obj?.GetType().GetField(privateField, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(obj);
    }
}
