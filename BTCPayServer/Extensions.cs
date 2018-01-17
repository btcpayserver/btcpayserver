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

namespace BTCPayServer
{
    public static class Extensions
    {
        public static bool SupportDropColumn(this Microsoft.EntityFrameworkCore.Migrations.Migration migration, string activeProvider)
        {
            return activeProvider != "Microsoft.EntityFrameworkCore.Sqlite";
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
            if (str.EndsWith("/"))
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

        public static IServiceCollection ConfigureBTCPayServer(this IServiceCollection services, IConfiguration conf)
        {
            services.Configure<BTCPayServerOptions>(o =>
            {
                o.LoadArgs(conf);
            });
            return services;
        }


        public static BitIdentity GetBitIdentity(this Controller controller, bool throws = true)
        {
            if (!(controller.User.Identity is BitIdentity))
                return throws ? throw new UnauthorizedAccessException("no-bitid") : (BitIdentity)null;
            return (BitIdentity)controller.User.Identity;
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
