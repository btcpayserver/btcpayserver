using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Lightning;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Lightning
{
    public class LndMigrationHelper
    {
        public LndMigrationHelper()
        {
        }

        public LndSeedFile UnlockFile { get; set; }

        public bool IsSeedlessLndNotice { get; set; }
        public bool PerformDetection(BTCPayServerOptions _BtcpayServerOptions, string connStr, string cryptoCode)
        {
            // by default we don't show seedless LND notice
            IsSeedlessLndNotice = false;

            var isLndConnString = !String.IsNullOrEmpty(connStr) &&
                (connStr.Contains("lnd-rest", StringComparison.OrdinalIgnoreCase) ||
                connStr.Contains("lnd-grpc", StringComparison.OrdinalIgnoreCase));
            if (!isLndConnString)
            {
                // if lightning connection string is not LND, return
                return false;
            }

            if (!_BtcpayServerOptions.LndSeedPath.ContainsKey(cryptoCode))
            {
                // if LNDSEEDPATH is not set for crypto, don't run check
                return false;
            }
                       
            var lndSeedFilePath = _BtcpayServerOptions.LndSeedPath[cryptoCode];
            if (String.IsNullOrEmpty(lndSeedFilePath))
            {
                // if LNDSEEDPATH is empty, don't run check
                return false;
            }

            
            // if we got this far, it means we have all settings and it's LND connection string
            // legacy settings initalized LND as seedless, so to get IsSeedlessLnd = false, we need to do check
            IsSeedlessLndNotice = true;
            try
            {
                // with new setting we'll try to check if unlock file existits and is in right format
                // if that's true and parsing passes, we know this is not seedless LND
                if (!String.IsNullOrEmpty(lndSeedFilePath) && File.Exists(lndSeedFilePath))
                {
                    var unlockFile = File.ReadAllText(lndSeedFilePath);
                    UnlockFile = JsonConvert.DeserializeObject<LndSeedFile>(unlockFile);

                    if (!String.IsNullOrEmpty(UnlockFile.wallet_password))
                    {
                        IsSeedlessLndNotice = false;
                        return true;
                    }
                }
            }
            catch
            {
            }

            return true;
        }

        public class LndSeedFile
        {
            public string wallet_password { get; set; }
            public List<string> cipher_seed_mnemonic { get; set; }
        }
    }
}
