﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BTCPayServer.Models.ServerViewModels
{
    public class LndSeedBackupViewModel
    {
        public bool IsWalletUnlockPresent { get; set; }

        public string WalletPassword { get; set; }

        public List<string> Seed { get; set; }


        public async Task<bool> RemoveSeedAndWrite(string lndSeedFilePath)
        {
            var removedDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ssZ", CultureInfo.InvariantCulture);
            var seedFile = new LndSeedFile
            {
                wallet_password = WalletPassword,
                cipher_seed_mnemonic = new List<string> { $"Seed removed on {removedDate}" }
            };
            var json = JsonConvert.SerializeObject(seedFile);
            try
            {
                await File.WriteAllTextAsync(lndSeedFilePath, json);
                return true;
            }
            catch
            {
                // file access exception and such
                return false;
            }
        }


        public static LndSeedBackupViewModel Parse(string lndSeedFilePath)
        {
            try
            {
                if (!String.IsNullOrEmpty(lndSeedFilePath) && File.Exists(lndSeedFilePath))
                {
                    var unlockFileContents = File.ReadAllText(lndSeedFilePath);
                    var unlockFile = JsonConvert.DeserializeObject<LndSeedFile>(unlockFileContents);

                    if (!String.IsNullOrEmpty(unlockFile.wallet_password))
                    {
                        return new LndSeedBackupViewModel
                        {
                            WalletPassword = unlockFile.wallet_password,
                            Seed = unlockFile.cipher_seed_mnemonic,
                            IsWalletUnlockPresent = true,
                        };
                    }
                }
            }
            catch
            {
            }

            return new LndSeedBackupViewModel();
        }

        private class LndSeedFile
        {
            public string wallet_password { get; set; }
            public List<string> cipher_seed_mnemonic { get; set; }
        }
    }
}
