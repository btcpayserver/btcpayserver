using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using Microsoft.Extensions.Logging;

namespace BTCPayServer
{
    // All logic that would otherwise be duplicated across solution goes into this utility class
    // ~If~ Once this starts growing out of control, begin extracting action logic classes out of here
    // Also some of logic in here may be result of parallel development of Greenfield API
    // It's much better that we extract those common methods then copy paste and maintain same code across codebase
    internal static class ActionLogicExtensions
    {
        internal static async Task FirstAdminRegistered(this SettingsRepository settingsRepository, PoliciesSettings policies,
            bool updateCheck, bool disableRegistrations, Logs logs)
        {
            if (updateCheck)
            {
                logs.PayServer.LogInformation("First admin created, enabling checks for new versions");
                policies.CheckForNewVersions = updateCheck;
            }

            if (disableRegistrations)
            {
                // Once the admin user has been created lock subsequent user registrations (needs to be disabled for unit tests that require multiple users).
                logs.PayServer.LogInformation("First admin created, disabling subscription (disable-registration is set to true)");
                policies.LockSubscription = true;
            }

            if (updateCheck || disableRegistrations)
                await settingsRepository.UpdateSetting(policies);
        }
    }
}
