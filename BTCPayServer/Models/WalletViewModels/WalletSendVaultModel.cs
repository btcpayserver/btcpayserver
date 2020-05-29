﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletSendVaultModel
    {
        public string WalletId { get; set; }
        public string WebsocketPath { get; set; }
        public SigningContextModel SigningContext { get; set; } = new SigningContextModel();
    }
}
