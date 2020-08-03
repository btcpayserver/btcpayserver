using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.HostedServices
{
    public class NewVersionCheckerHostedService : BaseAsyncService
    {
        private readonly SettingsRepository _settingsRepository;
        private readonly BTCPayServerEnvironment _env;
        private readonly NotificationSender _notificationSender;
        private readonly IVersionFetcher _versionFetcher;

        public NewVersionCheckerHostedService(SettingsRepository settingsRepository, BTCPayServerEnvironment env,
            NotificationSender notificationSender, IVersionFetcher versionFetcher)
        {
            _settingsRepository = settingsRepository;
            _env = env;
            _notificationSender = notificationSender;
            _versionFetcher = versionFetcher;
        }

        internal override Task[] InitializeTasks()
        {
            return new Task[] { CreateLoopTask(LoopVersionCheck) };
        }

        protected async Task LoopVersionCheck()
        {
            try
            {
                await ProcessVersionCheck();
            }
            catch (Exception ex)
            {
                Logs.Events.LogError(ex, "Error while performing new version check");
            }
            await Task.Delay(TimeSpan.FromDays(1), Cancellation);
        }

        public async Task ProcessVersionCheck()
        {
            var policies = await _settingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
            if (policies.CheckForNewVersions)
            {
                var tag = await _versionFetcher.Fetch(Cancellation);
                if (tag != null && tag != _env.Version)
                {
                    var dh = await _settingsRepository.GetSettingAsync<NewVersionCheckerDataHolder>() ?? new NewVersionCheckerDataHolder();
                    if (dh.LastVersion != tag)
                    {
                        await _notificationSender.SendNotification(new AdminScope(), new NewVersionNotification(tag));

                        dh.LastVersion = tag;
                        await _settingsRepository.UpdateSetting(dh);
                    }
                }
            }
        }
    }

    public class NewVersionCheckerDataHolder
    {
        public string LastVersion { get; set; }
    }

    public interface IVersionFetcher
    {
        Task<string> Fetch(CancellationToken cancellation);
    }

    public class GithubVersionFetcher : IVersionFetcher
    {
        private readonly HttpClient _httpClient;
        private readonly Uri _updateurl;
        public GithubVersionFetcher(IHttpClientFactory httpClientFactory, BTCPayServerOptions options)
        {
            _httpClient = httpClientFactory.CreateClient(nameof(GithubVersionFetcher));
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "BTCPayServer/NewVersionChecker");

            _updateurl = options.UpdateUrl;
        }

        private static readonly Regex _releaseVersionTag = new Regex("^(v[1-9]+(\\.[0-9]+)*(-[0-9]+)?)$");
        public async Task<string> Fetch(CancellationToken cancellation)
        {
            if (_updateurl == null)
                return null;

            using (var resp = await _httpClient.GetAsync(_updateurl, cancellation))
            {
                var strResp = await resp.Content.ReadAsStringAsync();
                if (resp.IsSuccessStatusCode)
                {
                    var jobj = JObject.Parse(strResp);
                    var tag = jobj["tag_name"].ToString();

                    var isReleaseVersionTag = _releaseVersionTag.IsMatch(tag);
                    return isReleaseVersionTag ? tag : null;
                }
                else
                {
                    Logs.Events.LogWarning($"Unsuccessful status code returned during new version check. " +
                        $"Url: {_updateurl}, HTTP Code: {resp.StatusCode}, Response Body: {strResp}");
                }
            }

            return null;
        }
    }
}
