using System;
using System.Collections.Generic;
using BTCPayServer.Services;
using Robotify.AspNetCore;

namespace BTCPayServer.Hosting
{
    public class BTCPayRobotProvider : IRobotifyRobotGroupProvider
    {
        private readonly SettingsRepository _SettingsRepository;

        public BTCPayRobotProvider(SettingsRepository settingsRepository)
        {
            _SettingsRepository = settingsRepository;
        }
        public IEnumerable<RobotGroup> Get()
        {
            var settings = _SettingsRepository.GetSettingAsync<PoliciesSettings>().GetAwaiter().GetResult();
            if (settings.DiscourageSearchEngines)
            {
                yield return new RobotGroup()
                {
                    UserAgent = "*",
                    Disallow = new[] {"/"}
                };
            }
            else
            {
                yield return new RobotGroup()
                {
                    UserAgent = "*",
                    Disallow = Array.Empty<string>()
                };
            }
            
        }
    }
}