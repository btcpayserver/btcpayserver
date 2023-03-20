using System.Collections.Generic;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Models.CustodianAccountViewModels;

public class WithdrawalPrepareViewModel : WithdrawalSimulationResponseData
{
    public string ErrorMessage { get; set; }
    public string[] BadConfigFields { get; set; }

    public WithdrawalPrepareViewModel(string paymentMethod, string asset, string accountId, string custodianCode,
        List<LedgerEntryData> ledgerEntries, decimal minQty, decimal maxQty) : base(paymentMethod, asset, accountId,
        custodianCode, ledgerEntries, minQty, maxQty)
    {
    }

    public WithdrawalPrepareViewModel(WithdrawalSimulationResponseData simulateWithdrawal) : base(
        simulateWithdrawal.PaymentMethod, simulateWithdrawal.Asset, simulateWithdrawal.AccountId,
        simulateWithdrawal.CustodianCode, simulateWithdrawal.LedgerEntries, simulateWithdrawal.MinQty,
        simulateWithdrawal.MaxQty)
    {
    }

    public WithdrawalPrepareViewModel() : base(null, null, null, null, null, null, null)
    {
    }
}
