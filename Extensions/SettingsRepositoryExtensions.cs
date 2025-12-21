#nullable enable
using BTCPayServer.NTag424;
using BTCPayServer.Services;
using static BTCPayServer.Controllers.UIBoltcardController;
using System.Threading.Tasks;

namespace BTCPayServer;
public static class SettingsRepositoryExtensions
{
    public static async Task<IssuerKey> GetIssuerKey(this SettingsRepository settingsRepository, BTCPayServerEnvironment env)
    {
        var settings = await settingsRepository.GetSettingAsync<BoltcardSettings>(nameof(BoltcardSettings));
        AESKey issuerKey;
        if (settings?.IssuerKey is byte[] bytes)
        {
            issuerKey = new AESKey(bytes);
        }
        else
        {
            issuerKey = env.CheatMode && env.IsDeveloping ? FixedKey() : AESKey.Random();
            settings = new BoltcardSettings() { IssuerKey = issuerKey.ToBytes() };
            await settingsRepository.UpdateSetting(settings, nameof(BoltcardSettings));
        }
        return new IssuerKey(issuerKey);
    }
    public static AESKey FixedKey()
    {
        byte[] v = new byte[16];
        v[0] = 1;
        return new AESKey(v);
    }
}
