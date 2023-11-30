#nullable enable
using System;
using Dapper;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers.GreenField;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Models.StoreReportsViewModels;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using System.Text.Json.Nodes;
using System.Threading;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NBitcoin.DataEncoders;
using System.Net;

namespace BTCPayServer.Controllers;

public partial class UIReportsController
{
    private IList<IList<object?>> Generate(IList<StoreReportResponse.Field> fields)
    {
        var rand = new Random();
        int rowCount = 1_000;
        List<object?> row = new List<object?>();
        List<IList<object?>> result = new List<IList<object?>>();
        for (int i = 0; i < rowCount; i++)
        {
            int fi = 0;
            foreach (var f in fields)
            {
                row.Add(GenerateData(fields, f, fi, row, rand));
                fi++;
            }
            result.Add(row);
            row = new List<object?>();
        }
        return result;
    }

    private object? GenerateData(IList<StoreReportResponse.Field> fields, StoreReportResponse.Field f, int fi, List<object?> row, Random rand)
    {
        byte[] GenerateBytes(int count)
        {
            var bytes = new byte[count];
            rand.NextBytes(bytes);
            return bytes;
        }
        T TakeOne<T>(params T[] v)
        {
            return v[rand.NextInt64(0, v.Length)];
        }
        decimal GenerateDecimal(decimal from, decimal to, int precision)
        {
            decimal range = to - from;
            decimal randomValue = ((decimal)rand.NextDouble() * range) + from;
            return decimal.Round(randomValue, precision);
        }

        var fiatCurrency = rand.NextSingle() > 0.2 ? "USD" : TakeOne("JPY", "EUR", "CHF");
        var cryptoCurrency = rand.NextSingle() > 0.2 ? "BTC" : TakeOne("LTC", "DOGE", "DASH");
        
        if (f.Type == "invoice_id")
            return Encoders.Base58.EncodeData(GenerateBytes(20));
        if (f.Type == "boolean")
            return GenerateBytes(1)[0] % 2 == 0;
        if (f.Name == "PaymentType")
            return TakeOne("On-Chain", "Lightning");
        if (f.Name == "PaymentId")
            if (row[fi -1] is "On-Chain")
                return Encoders.Hex.EncodeData(GenerateBytes(32)) + "-" + rand.NextInt64(0, 4);
            else
                return Encoders.Hex.EncodeData(GenerateBytes(32));
        if (f.Name == "Address")
            return Encoders.Bech32("bc1").Encode(0, GenerateBytes(20));
        if (f.Name == "Crypto")
            return cryptoCurrency;
        if (f.Name == "CryptoAmount")
            return DisplayFormatter.ToFormattedAmount(GenerateDecimal(0.1m, 5m, 8), cryptoCurrency);
        if (f.Name == "LightningAddress")
            return TakeOne("satoshi", "satosan", "satoichi") + "@bitcoin.org";
        if (f.Name == "BalanceChange")
            return GenerateDecimal(-5.0m, 5.0m, 8);
        if (f.Type == "datetime")
            return DateTimeOffset.UtcNow - TimeSpan.FromHours(rand.Next(0, 24 * 30 * 6)) - TimeSpan.FromMinutes(rand.Next(0, 60));
        if (f.Name == "Product")
            return TakeOne("green-tea", "black-tea", "oolong-tea", "coca-cola");
        if (f.Name == "State")
            return TakeOne("Settled", "Processing");
        if (f.Name == "AppId")
            return TakeOne("AppA", "AppB");
        if (f.Name == "Quantity")
            return TakeOne(1, 2, 3, 4, 5);
        if (f.Name == "Currency")
            return fiatCurrency;
        if (f.Name == "CurrencyAmount")
        {
            var curr = row[fi - 1]?.ToString();
            var value = curr switch
            {
                "USD" or "EUR" or "CHF" => GenerateDecimal(100.0m, 10_000m, 2),
                "JPY" => GenerateDecimal(10_000m, 1000_0000, 0),
                _ => GenerateDecimal(100.0m, 10_000m, 2)
            };
            return DisplayFormatter.ToFormattedAmount(value, curr);
        }
        if (f.Type == "tx_id")
            return Encoders.Hex.EncodeData(GenerateBytes(32));
        if (f.Name == "Rate")
        {
            var curr = row[fi - 1]?.ToString();
            var value = curr switch
            {
                "USD" or "EUR" or "CHF" => GenerateDecimal(30_000m, 60_000, 2),
                "JPY" => GenerateDecimal(400_0000m, 1000_0000m, 0),
                _ => GenerateDecimal(30_000m, 60_000, 2)
            };
            return DisplayFormatter.ToFormattedAmount(value, curr);
        }
        return null;
    }
}
