using System;
using System.Security.Cryptography;
using System.Text;

namespace BTCPayServer.Data
{
    public record StoreInvitationRow(
        string StoreId,
        string UserId,
        string RoleId,
        string InvitedByUserId,
        DateTime Created,
        DateTime ExpiresAt)
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
        public bool IsExpired() => ExpiresAt < DateTime.UtcNow;
        public static string HashToken(string token)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }
    public sealed record StoreInvitationExtendedRow(
        string StoreId,
        string UserId,
        string RoleId,
        string InvitedByUserId,
        DateTime Created,
        DateTime ExpiresAt,
        string UserEmail,
        string StoreName) : StoreInvitationRow(StoreId, UserId, RoleId, InvitedByUserId, Created, ExpiresAt);
}
