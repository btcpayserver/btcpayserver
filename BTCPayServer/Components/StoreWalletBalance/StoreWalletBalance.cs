using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBXplorer;
using NBXplorer.Client;
using static BTCPayServer.Components.StoreWalletBalance.StoreWalletBalanceViewModel;

namespace BTCPayServer.Components.StoreWalletBalance;

public enum StoreWalletBalanceType
{
    Today,
    ThisWeek,
    ThisMonth,
    ThisYear,
    Ever
}
public class StoreWalletBalance : ViewComponent
{
    private const string CryptoCode = "BTC";
    private const int PointCount = 30;
    private const int LabelCount = 7;

    private readonly StoreRepository _storeRepo;
    private readonly UserManager<ApplicationUser> _userManager;

    public StoreWalletBalance(
        StoreRepository storeRepo,
        UserManager<ApplicationUser> userManager,
        BTCPayNetworkProvider networkProvider,
        NBXplorerConnectionFactory connectionFactory)
    {
        _storeRepo = storeRepo;
        _userManager = userManager;
        NetworkProvider = networkProvider;
        ConnectionFactory = connectionFactory;
    }

    public StoreWalletBalanceType Type { get; set; } = StoreWalletBalanceType.ThisMonth;

    public BTCPayNetworkProvider NetworkProvider { get; }
    public NBXplorerConnectionFactory ConnectionFactory { get; }

    public async Task<IViewComponentResult> InvokeAsync(StoreData store)
    {
        var userId = _userManager.GetUserId(UserClaimsPrincipal);

        // https://github.com/dgarage/NBXplorer/blob/master/docs/Postgres-Schema.md

        var vm = new StoreWalletBalanceViewModel
        {
            Store = store,
            CryptoCode = CryptoCode
        };


        if (ConnectionFactory.Available)
        {
            var derivationSettings = store.GetDerivationSchemeSettings(NetworkProvider, CryptoCode);
            if (derivationSettings != null)
            {
                var wallet_id = derivationSettings.GetNBXWalletId();
                await using var conn = await ConnectionFactory.OpenConnection();

                var to = DateTimeOffset.UtcNow;
                var from = to - (Type switch
                {
                    StoreWalletBalanceType.Today => TimeSpan.FromDays(1.0),
                    StoreWalletBalanceType.ThisMonth => TimeSpan.FromDays(30.0),
                    StoreWalletBalanceType.ThisWeek => TimeSpan.FromDays(7.0),
                    StoreWalletBalanceType.ThisYear => TimeSpan.FromDays(365),
                    _ => TimeSpan.Zero
                });
                if (to == from)
                {
                    var minDate = await conn.ExecuteScalarAsync<DateTime?>("SELECT MIN(seen_at) FROM wallets_history code=@code AND wallet_id=@wallet_id",
                        new
                        {
                            code = CryptoCode,
                            wallet_id
                        });
                    if (minDate is DateTime t)
                        from = t;
                    else
                        from = to - TimeSpan.FromDays(1.0);
                }

                var interval = TimeSpan.FromTicks((to - from).Ticks / PointCount);

                var rows = await conn.QueryAsync("SELECT date, to_btc(balance) balance FROM get_wallets_histogram(@wallet_id, @code, '', @from, @to, @interval)",
                        new
                        {
                            wallet_id,
                            code = CryptoCode,
                            from = from,
                            to = to,
                            interval = interval
                        });
                vm.Series = new List<decimal>(PointCount);
                vm.Labels = new List<string>(PointCount);
                var labelEvery = PointCount / LabelCount;
                foreach (var r in rows)
                {
                    if (vm.Series.Count % labelEvery == 0)
                        vm.Labels.Add(((DateTime)r.date).ToShortDateString());
                    else
                        vm.Labels.Add(String.Empty);
                    vm.Series.Add((decimal)r.balance);
                }
                vm.Balance = await conn.ExecuteScalarAsync<decimal>("SELECT to_btc(available_balance) FROM wallets_balances WHERE wallet_id=@wallet_id", new { wallet_id });
            }
        }
        return View(vm);
    }
}
