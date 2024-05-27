using System;
using System.Collections.Generic;
using BTCPayServer.Lightning;

namespace BTCPayApp.CommonServer.Models;

public partial class LightningPayment
{
    public string PaymentHash { get; set; }
    public string? PaymentId { get; set; }
    public string? Preimage { get; set; }
    public string? Secret { get; set; }
    public bool Inbound { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public long Value { get; set; }
    public LightningPaymentStatus Status { get; set; }

    //you can have multiple requests generated for the same payment hash, but once you reveal the preimage, you should reject any attempt to pay the same payment hash
    public List<string> PaymentRequests { get; set; }
}

public class AppUserInfo
{
    public string? UserId { get; set; }
    public string? Email { get; set; }
    public IEnumerable<string>? Roles { get; set; }
    public IEnumerable<AppUserStoreInfo>? Stores { get; set; }

    public static bool Equals(AppUserInfo? x, AppUserInfo? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null)) return false;
        if (ReferenceEquals(y, null)) return false;
        if (x.GetType() != y.GetType()) return false;
        return x.UserId == y.UserId && x.Email == y.Email && Equals(x.Roles, y.Roles) && Equals(x.Stores, y.Stores);
    }
}

public class AppUserStoreInfo
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? RoleId { get; set; }
    public bool Archived { get; set; }
    public IEnumerable<string>? Permissions { get; set; }
}
