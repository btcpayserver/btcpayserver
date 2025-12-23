using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Services;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.HostedServices
{
    public class NewVersionCheckerDataHolder
    {
        public string LastVersion { get; set; }
    }

    public interface IVersionFetcher
    {
        Task<string> Fetch(CancellationToken cancellation);
    }

    public class GithubVersionFetcher : IPeriodicTask, IVersionFetcher
    {
        private readonly HttpClient _httpClient;
        private readonly Uri _updateurl;

        public GithubVersionFetcher(IHttpClientFactory httpClientFactory,
            BTCPayServerOptions options, ILogger<GithubVersionFetcher> logger, SettingsRepository settingsRepository,
            BTCPayServerEnvironment environment, NotificationSender notificationSender)
        {
            _logger = logger;
            _settingsRepository = settingsRepository;
            _environment = environment;
            _notificationSender = notificationSender;
            _httpClient = httpClientFactory.CreateClient(nameof(GithubVersionFetcher));
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "BTCPayServer/NewVersionChecker");

            _updateurl = options.UpdateUrl;
        }

        private static readonly Regex _releaseVersionTag = new Regex("^(v[1-9]+(\\.[0-9]+)*(-[0-9]+)?)$");
        private readonly ILogger<GithubVersionFetcher> _logger;
        private readonly SettingsRepository _settingsRepository;
        private readonly BTCPayServerEnvironment _environment;
        private readonly NotificationSender _notificationSender;

        public virtual async Task<string> Fetch(CancellationToken cancellation)
        {
            if (_updateurl == null)
                return null;

            using var resp = await _httpClient.GetAsync(_updateurl, cancellation);
            var strResp = await resp.Content.ReadAsStringAsync(cancellation);
            if (resp.IsSuccessStatusCode)
            {
                var jobj = JObject.Parse(strResp);
                var tag = jobj["tag_name"].ToString();

                var isReleaseVersionTag = _releaseVersionTag.IsMatch(tag);
                if (isReleaseVersionTag)
                {
                    return tag.TrimStart('v');
                }
                else
                {
                    return null;
                }
            }
            else
            {
                _logger.LogWarning($"Unsuccessful status code returned during new version check. " +
                                   $"Url: {_updateurl}, HTTP Code: {resp.StatusCode}, Response Body: {strResp}");
            }

            return null;
        }

        public async Task Do(CancellationToken cancellationToken)
        {
            var policies = await _settingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
            if (policies.CheckForNewVersions)
            {
                var tag = await Fetch(cancellationToken);
                if (tag != null && tag != _environment.Version)
                {
                    var dh = await _settingsRepository.GetSettingAsync<NewVersionCheckerDataHolder>() ??
                             new NewVersionCheckerDataHolder();
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
}
