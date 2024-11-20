#nullable enable
using System.Collections.Generic;

namespace BTCPayServer.Client.App.Models;

public class AppUserInfo
{
    public string? UserId { get; set; }
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? ImageUrl { get; set; }
    public IEnumerable<string>? Roles { get; set; }
    public IEnumerable<AppUserStoreInfo>? Stores { get; set; }

    public void SetInfo(string email, string? name, string? imageUrl)
    {
        Email = email;
        Name = name;
        ImageUrl = imageUrl;
    }

    public static bool Equals(AppUserInfo? x, AppUserInfo? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null)) return false;
        if (ReferenceEquals(y, null)) return false;
        if (x.GetType() != y.GetType()) return false;
        return x.UserId == y.UserId && x.Email == y.Email &&
               x.Name == y.Name && x.ImageUrl == y.ImageUrl &&
               Equals(x.Roles, y.Roles) && Equals(x.Stores, y.Stores);
    }
}

public class AppUserStoreInfo
{
    public string Id { get; set; } = null!;
    public string? Name { get; set; }
    public string? LogoUrl { get; set; }
    public string? RoleId { get; set; }
    public string? PosAppId { get; set; }
    public string? DefaultCurrency { get; set; }
    public bool Archived { get; set; }
    public IEnumerable<string>? Permissions { get; set; }
}
