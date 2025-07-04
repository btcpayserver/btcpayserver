using System;
using System.Collections.Generic;

namespace BTCPayServer.Models.WalletViewModels;

public class ReservedAddressesViewModel
{
    public string WalletId { get; set; }
    public string CryptoCode { get; set; }
    public List<ReservedAddress> Addresses { get; set; }
}

public class ReservedAddress
{
    public string Address { get; set; }
    public List<TransactionTagModel> Labels { get; set; } = new();
    public DateTimeOffset? ReservedAt { get; set; }
}

