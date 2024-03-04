using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using Dapper;

namespace BTCPayServer.Services.Wallets;

public enum WalletHistogramType
{
    Week,
    Month,
    Year
}

public class WalletHistogramService
{
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly NBXplorerConnectionFactory _connectionFactory;

    public WalletHistogramService(
        PaymentMethodHandlerDictionary handlers,
        NBXplorerConnectionFactory connectionFactory)
    {
        _handlers = handlers;
        _connectionFactory = connectionFactory;
    }

    public async Task<WalletHistogramData> GetHistogram(StoreData store, WalletId walletId, WalletHistogramType type)
    {
        // https://github.com/dgarage/NBXplorer/blob/master/docs/Postgres-Schema.md
        if (_connectionFactory.Available)
        {
            var derivationSettings = store.GetDerivationSchemeSettings(_handlers, walletId.CryptoCode);
            if (derivationSettings != null)
            {
                var network = _handlers.GetBitcoinHandler(walletId.CryptoCode);
                var wallet_id = derivationSettings.GetNBXWalletId(network.Network.NBitcoinNetwork);
                await using var conn = await _connectionFactory.OpenConnection();

                var code = walletId.CryptoCode;
                var to = DateTimeOffset.UtcNow;
                var labelCount = 6;
                (var days, var pointCount) = type switch
                {
                    WalletHistogramType.Week => (7, 30),
                    WalletHistogramType.Month => (30, 30),
                    WalletHistogramType.Year => (365, 30),
                    _ => throw new ArgumentException($"WalletHistogramType {type} does not exist.")
                };
                var from = to - TimeSpan.FromDays(days);
                var interval = TimeSpan.FromTicks((to - from).Ticks / pointCount);
                var balance = await conn.ExecuteScalarAsync<decimal>(
                    "SELECT to_btc(available_balance) FROM wallets_balances WHERE wallet_id=@wallet_id AND code=@code AND asset_id=''",
                    new { code, wallet_id });
                var rows = await conn.QueryAsync("SELECT date, to_btc(balance) balance FROM get_wallets_histogram(@wallet_id, @code, '', @from, @to, @interval)",
                    new { code, wallet_id, from, to, interval });
                var data = rows.AsList();
                var series = new List<decimal>(pointCount);
                var labels = new List<string>(labelCount);
                var labelEvery = pointCount / labelCount;
                for (int i = 0; i < data.Count; i++)
                {
                    var r = data[i];
                    series.Add((decimal)r.balance);
                    labels.Add((i % labelEvery == 0)
                        ? ((DateTime)r.date).ToString("MMM dd", CultureInfo.InvariantCulture)
                        : null);
                }
                series[^1] = balance;
                return new WalletHistogramData
                {
                    Series = series,
                    Labels = labels,
                    Balance = balance,
                    Type = type
                };
            }
        }

        return null;
    }
}

public class WalletHistogramData
{
    public WalletHistogramType Type { get; set; }
    public List<decimal> Series { get; set; }
    public List<string> Labels { get; set; }
    public decimal Balance { get; set; }
}
