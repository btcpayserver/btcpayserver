using System;
using System.IO;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Plugins.Liquid.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Liquid.Services
{
    public class CustomLiquidAssetsRepository
    {
        private readonly ILogger<CustomLiquidAssetsRepository> _logger;
        private readonly DataDirectories _options;
        private string File => Path.Combine(_options.DataDir, "custom-liquid-assets.json");

        public CustomLiquidAssetsRepository(ILogger<CustomLiquidAssetsRepository> logger, IConfiguration configuration)
        {
            _logger = logger;
            _options = new DataDirectories().Configure(configuration);
        }

        public CustomLiquidAssetsSettings Get()
        {
            try
            {
                if (System.IO.File.Exists(File))
                {
                    return JObject.Parse(System.IO.File.ReadAllText(File)).ToObject<CustomLiquidAssetsSettings>();
                }
            }

            catch (Exception e)
            {
                _logger.LogError(e, "could not parse custom liquid assets file");
            }

            return new CustomLiquidAssetsSettings();
        }

        public async Task Set(CustomLiquidAssetsSettings settings)
        {
            try
            {
                await System.IO.File.WriteAllTextAsync(File, JObject.FromObject(settings).ToString(Formatting.Indented));

                ChangesPending = true;
            }

            catch (Exception e)
            {
                _logger.LogError(e, "could not write custom liquid assets file");
            }
        }

        public bool ChangesPending { get; private set; }
    }
}
