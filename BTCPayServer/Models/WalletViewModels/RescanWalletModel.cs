using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using NBXplorer.Models;

namespace BTCPayServer.Models.WalletViewModels
{
    public class RescanWalletModel
    {
        public bool IsServerAdmin { get; set; }
        public bool IsSupportedByCurrency { get; set; }
        public bool IsFullySync { get; set; }
        public bool IsSegwit { get; set; }
        public bool Ok => IsServerAdmin && IsSupportedByCurrency && IsFullySync && IsSegwit;

        [Range(1000, 10_000)]
        public int BatchSize { get; set; } = 3000;
        [Range(0, 10_000_000)]
        public int StartingIndex { get; set; } = 0;

        [Range(100, 100000)]
        public int GapLimit { get; set; } = 10000;

        public int? Progress { get; set; }
        public string PreviousError { get; set; }
        public ScanUTXOProgress LastSuccess { get; internal set; }
        public string TimeOfScan { get; internal set; }
        public string RemainingTime { get; internal set; }
    }
}
