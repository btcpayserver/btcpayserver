using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Lightning
{
    public class LndMigrationHelper
    {
        private readonly LightningClientFactoryService _lightningClientFactory;

        public LndMigrationHelper(LightningClientFactoryService lightningClientFactory,
            BTCPayNetworkProvider networkProvider)
        {
            _lightningClientFactory = lightningClientFactory;
        }

        public LndSeedFile UnlockFile { get; set; }

        public bool IsSeedlessLnd { get; set; }
        public async Task PerformDetection()
        {
            // by default all LND instalations were started as seedless
            IsSeedlessLnd = true;

            try
            {
                // with new setting we'll try to check if unlock file existits and is in right format
                // if that's true and parsing passes, we know this is not seedless LND
                var seedFilePath = Environment.GetEnvironmentVariable("BTCPAY_LND_SEEDPATH");
                if (!String.IsNullOrEmpty(seedFilePath) && File.Exists(seedFilePath))
                {
                    var unlockFile = File.ReadAllText(seedFilePath);
                    UnlockFile = JsonConvert.DeserializeObject<LndSeedFile>(unlockFile);

                    if (!String.IsNullOrEmpty(UnlockFile.wallet_password))
                    {
                        IsSeedlessLnd = false;
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        public class LndSeedFile
        {
            public string wallet_password { get; set; }
            public List<string> cipher_seed_mnemonic { get; set; }
        }
    }
}
