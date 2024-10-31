using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Hosting
{
    public class LoadTranslationsStartupTask : IStartupTask
    {
        public LoadTranslationsStartupTask(
            ILogger<LoadTranslationsStartupTask> logger,
            LocalizerService localizerService,
            IOptions<DataDirectories> dataDirectories)
        {
            DataDirectories = dataDirectories.Value;
            Logger = logger;
            LocalizerService = localizerService;
        }

        public DataDirectories DataDirectories { get; }
        public ILogger<LoadTranslationsStartupTask> Logger { get; }
        public LocalizerService LocalizerService { get; }

        class DictionaryFileMetadata
        {
            [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
            [JsonProperty("hash")]
            public uint256 Hash { get; set; }
        }
        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            // This load languages files from a [datadir]/Langs into the database
            // to make startup faster, we skip update if we see that files didn't changes
            // since the last time they got loaded.
            // We do this by comparing hashes of the current file, to the one stored in DB.
            if (Directory.Exists(DataDirectories.LangsDir))
            {
                var files = Directory.GetFiles(DataDirectories.LangsDir);
                if (files.Length > 0)
                {
                    Logger.LogInformation("Loading language files...");
                    var dictionaries = await LocalizerService.GetDictionaries();
                    foreach (var file in Directory.GetFiles(DataDirectories.LangsDir))
                    {
                        var langName = Path.GetFileName(file);
                        var dictionary = dictionaries.FirstOrDefault(d => d.DictionaryName == langName);
                        if (dictionary is null)
                            dictionary = await LocalizerService.CreateDictionary(langName, null, "File");
                        if (dictionary.Source != "File")
                        {
                            Logger.LogWarning($"Impossible to load language '{langName}', as it is already existing in the database, not initially imported by a File");
                            continue;
                        }
                        var savedHash = dictionary.Metadata.ToObject<DictionaryFileMetadata>().Hash;
                        var translations = Translations.CreateFromJson(File.ReadAllText(file));
                        var currentHash = new uint256(SHA256.HashData(Encoding.UTF8.GetBytes(translations.ToJsonFormat())));

                        if (savedHash != currentHash)
                        {
                            var newMetadata = (JObject)dictionary.Metadata.DeepClone();
                            newMetadata["hash"] = currentHash.ToString();
                            dictionary = dictionary with { Metadata = newMetadata };
                            Logger.LogInformation($"Updating dictionary '{langName}'");
                            await LocalizerService.Save(dictionary, translations);
                        }
                    }
                }
            }

            // Do not make startup longer for this
            _ = LocalizerService.Load();
        }
    }
}
