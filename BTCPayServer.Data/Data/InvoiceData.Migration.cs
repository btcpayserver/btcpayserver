using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO.Compression;
using System.IO;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Globalization;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using BTCPayServer.Migrations;
using Newtonsoft.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using System.Threading.Tasks;
using System.Threading;

namespace BTCPayServer.Data
{
    public partial class InvoiceData : MigrationInterceptor.IHasMigration
    {
        static HashSet<string> superflousProperties = new HashSet<string>()
        {
            "availableAddressHashes",
            "events",
            "refunds",
            "paidAmount",
            "historicalAddresses",
            "refundable",
            "status",
            "exceptionStatus",
            "storeId",
            "id",
            "txFee",
            "refundMail",
            "rate",
            "depositAddress",
            "currency",
            "price",
            "payments",
            "orderId",
            "buyerInformation",
            "productInformation",
            "derivationStrategy",
            "archived",
            "isUnderPaid",
            "requiresRefundEmail",
            "invoiceTime",
            "checkoutType",
            "customLogo",
            "customCSS"
        };

#pragma warning disable CS0618 // Type or member is obsolete
        public bool TryMigrate()
        {
            if (Currency is not null)
                return false;
            if (Blob is not (null or { Length: 0 }))
            {
                Blob2 = MigrationExtensions.Unzip(Blob);
                Blob2 = MigrationExtensions.SanitizeJSON(Blob2);
                Blob = null;
            }
            var blob = JObject.Parse(Blob2);
            if (blob["cryptoData"]?["BTC"] is not (null or { Type: JTokenType.Null }))
            {
                blob.Move(["rate"], ["cryptoData", "BTC", "rate"]);
                blob.Move(["txFee"], ["cryptoData", "BTC", "txFee"]);
            }
            blob.Move(["customerEmail"], ["metadata", "buyerEmail"]);
            foreach (var prop in (blob["cryptoData"] as JObject)?.Properties()?.ToList() ?? [])
            {
                // We should only change data for onchain
                if (prop.Name.Contains('_', StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value is JObject pm)
                    {
                        pm.Remove("depositAddress");
                        pm.Remove("feeRate");
                        pm.Remove("txFee");
                    }
                    continue;
                }
                if (prop.Value is JObject o)
                {
                    o.ConvertNumberToString("rate");
                    if (o["paymentMethod"] is JObject pm)
                    {
                        if (pm["networkFeeRate"] is null)
                            pm["networkFeeRate"] = o["feeRate"] ?? 0.0m;
                        if (pm["networkFeeMode"] is JValue { Type: JTokenType.Integer, Value: 0 or 0L })
                            pm.Remove("networkFeeMode");
                        if (pm["networkFeeMode"] is JValue { Type: JTokenType.Integer, Value: 2 or 2L })
                            pm["networkFeeRate"] = 0.0m;
                    }
                }
            }

            var metadata = blob.Property("metadata")?.Value as JObject;
            if (metadata is null)
            {
                metadata = new JObject();
                blob.Add("metadata", metadata);
            }
            foreach (var prop in (blob["buyerInformation"] as JObject)?.Properties()?.ToList() ?? [])
            {
                if (prop.Value?.Value<string>() is not null)
                    blob.Move(["buyerInformation", prop.Name], ["metadata", prop.Name]);
            }
            foreach (var prop in (blob["productInformation"] as JObject)?.Properties()?.ToList() ?? [])
            {
                if (prop.Name is "price" or "currency")
                    blob.Move(["productInformation", prop.Name], [prop.Name]);
                else if (prop.Value?.Value<string>() is not null)
                    blob.Move(["productInformation", prop.Name], ["metadata", prop.Name]);
            }
            blob.Move(["orderId"], ["metadata", "orderId"]);
            foreach (string prop in new string[] { "posData", "defaultLanguage", "notificationEmail", "notificationURL", "storeSupportUrl", "redirectURL" })
            {
                blob.RemoveIfNull(prop);
            }
            blob.RemoveIfValue<bool>("fullNotifications", false);
            if (blob["receiptOptions"] is JObject receiptOptions)
            {
                foreach (string prop in new string[] { "showQR", "enabled", "showPayments" })
                {
                    receiptOptions.RemoveIfNull(prop);
                }
            }

            {
                if (blob.Property("paymentTolerance") is JProperty { Value: { Type: JTokenType.Float } pv } prop)
                {
                    if (pv.Value<decimal>() == 0.0m)
                        prop.Remove();
                }
            }

            var posData = blob.Move(["posData"], ["metadata", "posData"]);
            if (posData is not null && posData.Value?.Type is JTokenType.String)
            {
                try
                {
                    posData.Value = JObject.Parse(posData.Value<string>());
                }
                catch
                {
                    posData.Remove();
                }
            }
            if (posData?.Type is JTokenType.Null)
                posData.Remove();

            if (blob["derivationStrategies"] is JValue { Type: JTokenType.String } v)
                blob["derivationStrategies"] = JObject.Parse(v.Value<string>());
            if (blob["derivationStrategies"] is JObject derivations)
            {
                foreach (var prop in derivations.Properties().ToList())
                {
                    // We should only change data for onchain
                    if (prop.Name.Contains('_', StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (prop.Value is JValue
                        {
                            Type: JTokenType.String,
                            Value: String { Length: > 0 } val
                        })
                    {
                        if (val[0] == '{')
                            derivations[prop.Name] = JObject.Parse(val);
                        else
                        {
                            if (val.Contains('-', StringComparison.OrdinalIgnoreCase))
                                derivations[prop.Name] = new JObject() { ["accountDerivation"] = val };
                            else
                                derivations[prop.Name] = null;
                        }
                    }
                    if (prop.Value is JObject derivation)
                    {
                        derivations[prop.Name] = derivation["accountDerivation"];
                    }
                }
            }

            if (blob["derivationStrategies"] is null && blob["derivationStrategy"] is not null)
            {
                // If it's NBX derivation strategy, keep it. Else just give up, it might be Electrum format and we shouldn't support
                // that anymore in the backend for long...
                if (blob["derivationStrategy"]?.Value<string>().Contains('-', StringComparison.OrdinalIgnoreCase) is true)
                    blob.Move(["derivationStrategy"], ["derivationStrategies", "BTC"]);
                else
                {
                    blob.Remove("derivationStrategy");
                    blob.Add("derivationStrategies", new JObject() { ["BTC"] = null });
                }
            }


            if (blob["type"]?.Value<string>() is "Standard")
                blob.Remove("type");
            foreach (var prop in new string[] { "extendedNotifications", "lazyPaymentMethods", "lazyPaymentMethods", "redirectAutomatically" })
            {
                if (blob[prop]?.Value<bool>() is false)
                    blob.Remove(prop);
            }

            blob.ConvertNumberToString("price");
            Currency = blob["currency"].Value<string>().ToUpperInvariant();
            var isTopup = blob["type"]?.Value<string>() is "TopUp";
            var amount = decimal.Parse(blob["price"].Value<string>(), CultureInfo.InvariantCulture);
            Amount = isTopup && amount == 0 ? null : decimal.Parse(blob["price"].Value<string>(), CultureInfo.InvariantCulture);
            foreach (var prop in superflousProperties)
                blob.Property(prop)?.Remove();
            if (blob["speedPolicy"] is JValue { Type: JTokenType.Integer, Value: 0 or 0L })
                blob.Remove("speedPolicy");
            blob.TryAdd("internalTags", new JArray());
            blob.TryAdd("receiptOptions", new JObject());

            foreach (var prop in ((JObject)blob["cryptoData"]).Properties())
            {
                if (prop.Name.EndsWith("_LightningLike", StringComparison.OrdinalIgnoreCase) ||
                    prop.Name.EndsWith("_LNURLPAY", StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value["paymentMethod"]?["PaymentHash"] is JObject)
                        prop.Value["paymentMethod"]["PaymentHash"] = JValue.CreateNull();
                    if (prop.Value["paymentMethod"]?["Preimage"] is JObject)
                        prop.Value["paymentMethod"]["Preimage"] = JValue.CreateNull();
                }
            }

            foreach (var prop in ((JObject)blob["cryptoData"]).Properties())
            {
                var crypto = prop.Name.Split(['_', '-']).First();
                if (blob.Move(["cryptoData", prop.Name, "rate"], ["rates", crypto]) is not null)
                    ((JObject)blob["rates"]).ConvertNumberToString(crypto);
            }
            blob.Move(["cryptoData"], ["prompts"]);
            var prompts = ((JObject)blob["prompts"]);
            foreach (var prop in prompts.Properties().ToList())
            {
                ((JObject)blob["prompts"]).RenameProperty(prop.Name, MigrationExtensions.MigratePaymentMethodId(prop.Name));
            }
            blob["derivationStrategies"] = blob["derivationStrategies"] ?? new JObject();
            foreach (var prop in ((JObject)blob["derivationStrategies"]).Properties().ToList())
            {
                ((JObject)blob["derivationStrategies"]).RenameProperty(prop.Name, MigrationExtensions.MigratePaymentMethodId(prop.Name));
            }

            foreach (var prop in prompts.Properties())
            {
                var prompt = prop.Value as JObject;
                if (prompt is null)
                    continue;
                prompt["currency"] = prop.Name.Split('-').First();
                prompt.RemoveIfNull("depositAddress");
                prompt.RemoveIfNull("txFee");
                prompt.RemoveIfNull("feeRate");

                prompt.RenameProperty("depositAddress", "destination");
                prompt.RenameProperty("txFee", "paymentMethodFee");

                var divisibility = MigrationExtensions.GetDivisibility(prop.Name);
                prompt.Add("divisibility", divisibility);
                if (prompt["paymentMethodFee"] is { Type: JTokenType.Integer } paymentMethodFee)
                {
                    prompt["paymentMethodFee"] = ((decimal)paymentMethodFee.Value<long>() / (decimal)Math.Pow(10, divisibility)).ToString(CultureInfo.InvariantCulture);
                    prompt.RemoveIfValue<string>("paymentMethodFee", "0");
                }
                prompt.Move(["paymentMethod"], ["details"]);
                prompt.Move(["feeRate"], ["details", "recommendedFeeRate"]);
                prompt.Move(["details", "networkFeeRate"], ["details", "paymentMethodFeeRate"]);
                prompt.Move(["details", "networkFeeMode"], ["details", "feeMode"]);
                if ((prompt["details"]?["Activated"])?.Value<bool>() is bool activated)
                {
                    ((JObject)prompt["details"]).Remove("Activated");
                    prompt["inactive"] = !activated;
                    prompt.RemoveIfValue<bool>("inactive", false);
                }
                if ((prompt["details"]?["activated"])?.Value<bool>() is bool activated2)
                {
                    ((JObject)prompt["details"]).Remove("activated");
                    prompt["inactive"] = !activated2;
                    prompt.RemoveIfValue<bool>("inactive", false);
                }
                var details = prompt["details"] as JObject ?? new JObject();
                details.RemoveIfValue<bool>("payjoinEnabled", false);
                details.RemoveIfNull("feeMode");
                if (details["feeMode"] is not (null or { Type: JTokenType.Null }))
                {
                    details["feeMode"] = details["feeMode"].Value<int>() switch
                    {
                        1 => "Always",
                        2 => "Never",
                        _ => null
                    };
                    details.RemoveIfNull("feeMode");
                }

                details.RemoveIfNull("BOLT11");
                details.RemoveIfNull("address");
                details.RemoveIfNull("Address");
                prompt.Move(["details", "BOLT11"], ["destination"]);
                prompt.Move(["details", "address"], ["destination"]);
                prompt.Move(["details", "Address"], ["destination"]);
                prompt.RenameProperty("Address", "destination");
                prompt.RenameProperty("BOLT11", "destination");

                details.Remove("LightningSupportedPaymentMethod");
                foreach (var o in detailsRemoveDefault)
                    details.RemoveIfNull(o);
                details.RemoveIfValue<decimal>("recommendedFeeRate", 0.0m);
                details.RemoveIfValue<decimal>("paymentMethodFeeRate", 0.0m);
                if (prop.Name.EndsWith("-CHAIN"))
                    blob.Move(["derivationStrategies", prop.Name], ["prompts", prop.Name, "details", "accountDerivation"]);

                var camel = new CamelCaseNamingStrategy();
                foreach (var p in details.Properties().ToList())
                {
                    var camelName = camel.GetPropertyName(p.Name, false);
                    if (camelName != p.Name)
                        details.RenameProperty(p.Name, camelName);
                }
            }

            if (blob["defaultPaymentMethod"] is not (null or { Type : JTokenType.Null }))
                blob["defaultPaymentMethod"] = MigrationExtensions.MigratePaymentMethodId(blob["defaultPaymentMethod"].Value<string>());
            blob.Remove("derivationStrategies");
            Status = Status switch
            {
                "new" => "New",
                "paid" => "Processing",
                "complete" or "confirmed" => "Settled",
                "expired" => "Expired",
                null or "invalid" => "Invalid",
                _ => throw new NotSupportedException($"Unknown Status for invoice ({Status})")
            };
            ExceptionStatus = ExceptionStatus switch
            {
                "marked" => "Marked",
                "paidLate" => "PaidLate",
                "paidPartial" => "PaidPartial",
                "paidOver" => "PaidOver",
                null or "" => "",
                _ => throw new NotSupportedException($"Unknown ExceptionStatus for invoice ({ExceptionStatus})")
            };
            blob["version"] = 3;
            Blob2 = blob.ToString(Formatting.None);
            return true;
        }

        [NotMapped]
        public bool Migrated { get; set; }
        static string[] detailsRemoveDefault =
            [
                "paymentMethodFeeRate",
                "keyPath",
                "BOLT11",
                "NodeInfo",
                "Preimage",
                "InvoiceId",
                "PaymentHash",
                "ProvidedComment",
                "GeneratedBoltAmount",
                "ConsumedLightningAddress",
                "PayRequest"
            ];

#pragma warning restore CS0618 // Type or member is obsolete
    }
}
