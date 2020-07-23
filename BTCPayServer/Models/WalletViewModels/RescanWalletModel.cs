using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NBXplorer.Models;

namespace BTCPayServer.Models.WalletViewModels
{
    public class RescanWalletModel
    {
        public bool IsServerAdmin { get; set; }
        public bool IsSupportedByCurrency { get; set; }
        public bool IsFullySync { get; set; }
        public bool Ok => IsServerAdmin && IsSupportedByCurrency && IsFullySync;

        [Range(1000, 10_000)]
        [DisplayName("Batch size")]
        public int BatchSize { get; set; } = 3000;
        [Range(0, 10_000_000)]
        [DisplayName("Starting index")]
        public int StartingIndex { get; set; } = 0;

        [Range(100, 100000)]
        [DisplayName("Gap limit")]
        public int GapLimit { get; set; } = 10000;

        public int? Progress { get; set; }
        public string PreviousError { get; set; }
        public ScanUTXOProgress LastSuccess { get; internal set; }
        public string TimeOfScan { get; internal set; }
        public string RemainingTime { get; internal set; }
    }
}
