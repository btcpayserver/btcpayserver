using System;
using System.Collections.Generic;

namespace BTCPayApp.CommonServer;

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
