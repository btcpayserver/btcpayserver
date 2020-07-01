using System;
using System.Collections.Generic;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Labels
{
    public class LabelFactory
    {
        private readonly LinkGenerator _linkGenerator;

        public LabelFactory(LinkGenerator linkGenerator)
        {
            _linkGenerator = linkGenerator;
        }

        public IEnumerable<Label> GetLabels(WalletBlobInfo walletBlobInfo, WalletTransactionInfo transactionInfo,
            HttpRequest request)
        {
            foreach (var label in transactionInfo.Labels)
            {
                if (walletBlobInfo.LabelColors.TryGetValue(label, out var color))
                {
                    yield return CreateLabel(label, color, request);
                }
            }
        }

        public IEnumerable<Label> GetLabels(WalletBlobInfo walletBlobInfo, HttpRequest request)
        {
            foreach (var kv in walletBlobInfo.LabelColors)
            {
                yield return CreateLabel(kv.Key, kv.Value, request);
            }
        }

        private Label CreateLabel(string value, string color, HttpRequest request)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (color == null)
                throw new ArgumentNullException(nameof(color));
            if (value.StartsWith("{", StringComparison.InvariantCultureIgnoreCase))
            {
                var jObj = JObject.Parse(value);
                if (jObj.ContainsKey("value"))
                {
                    var id = jObj.ContainsKey("id") ? jObj["id"].Value<string>() : string.Empty;
                    var idInLabel = string.IsNullOrEmpty(id) ? string.Empty : $"({id})";
                    switch (jObj["value"].Value<string>())
                    {
                        case "invoice":
                            return new Label()
                            {
                                RawValue = value,
                                Value = "invoice",
                                Color = color,
                                Tooltip = $"Received through an invoice {idInLabel}",
                                Link = string.IsNullOrEmpty(id)
                                    ? null
                                    : _linkGenerator.InvoiceLink(id, request.Scheme, request.Host, request.PathBase)
                            };
                        case "pj-exposed":
                            return new Label()
                            {
                                RawValue = value,
                                Value = "payjoin-exposed",
                                Color = color,
                                Tooltip = $"This utxo was exposed through a payjoin proposal for an invoice {idInLabel}",
                                Link = string.IsNullOrEmpty(id)
                                    ? null
                                    : _linkGenerator.InvoiceLink(id, request.Scheme, request.Host, request.PathBase)
                            };
                    }
                }
            }

            return new Label() { RawValue = value, Value = value, Color = color };
        }
    }
}
