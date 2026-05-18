using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Translations
{
    public class LoadTranslationsStartupTask(
        ILogger<LoadTranslationsStartupTask> logger,
        LocalizerService localizerService,
        IOptions<DataDirectories> dataDirectories)
        : IStartupTask
    {
        public DataDirectories DataDirectories { get; } = dataDirectories.Value;
        public ILogger<LoadTranslationsStartupTask> Logger { get; } = logger;
        public LocalizerService LocalizerService { get; } = localizerService;

        class TranslationFileMetadata
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
                    var availableTranslations = await LocalizerService.GetTranslations();
                    foreach (var file in files)
                    {
                        var langName = Path.GetFileName(file);
                        var translation = availableTranslations.FirstOrDefault(t => t.TranslationName == langName);
                        if (translation is null)
                            translation = await LocalizerService.CreateTranslation(langName, null, "File");
                        if (translation.Source != "File")
                        {
                            Logger.LogWarning($"Impossible to load language '{langName}', as it is already existing in the database, not initially imported by a File");
                            continue;
                        }
                        var savedHash = translation.Metadata.ToObject<TranslationFileMetadata>().Hash;
                        var translations = Translations.CreateFromJson(await File.ReadAllTextAsync(file, cancellationToken));
                        var currentHash = new uint256(SHA256.HashData(Encoding.UTF8.GetBytes(translations.ToJsonFormat())));

                        if (savedHash != currentHash)
                        {
                            var newMetadata = (JObject)translation.Metadata.DeepClone();
                            newMetadata["hash"] = currentHash.ToString();
                            translation = translation with { Metadata = newMetadata };
                            Logger.LogInformation($"Updating translation '{langName}'");
                            await LocalizerService.Save(translation, translations);
                        }
                    }
                }
            }

            // Do not make startup longer for this
            _ = LocalizerService.Load();
        }
    }
}
