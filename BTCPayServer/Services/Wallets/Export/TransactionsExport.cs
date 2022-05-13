using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using CsvHelper.Configuration;
using NBXplorer.Models;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Wallets.Export
{
    public class TransactionsExport
    {
        private readonly BTCPayWallet _wallet;
        private readonly WalletBlobInfo _walletBlob;
        private readonly Dictionary<string, WalletTransactionInfo> _walletTransactionsInfo;

        public TransactionsExport(BTCPayWallet wallet, WalletBlobInfo walletBlob, Dictionary<string, WalletTransactionInfo> walletTransactionsInfo)
        {
            _wallet = wallet;
            _walletBlob = walletBlob;
            _walletTransactionsInfo = walletTransactionsInfo;
        }

        public string Process(List<TransactionInformation> inputList, string fileFormat)
        {
            var list = inputList.Select(tx =>
            {
                var model = new ExportTransaction
                {
                    Id = tx.TransactionId.ToString(),
                    Timestamp = tx.Timestamp,
                    Positive = tx.BalanceChange.GetValue(_wallet.Network) >= 0,
                    Balance = tx.BalanceChange.ShowMoney(_wallet.Network),
                    IsConfirmed = tx.Confirmations != 0
                };
                
                if (_walletTransactionsInfo.TryGetValue(tx.TransactionId.ToString(), out var transactionInfo))
                {
                    model.Labels = transactionInfo.Labels?.Select(l => l.Value.Text).ToList();
                    model.Comment = transactionInfo.Comment;
                }

                return model;
            }).ToList();

            if (string.Equals(fileFormat, "json", StringComparison.OrdinalIgnoreCase))
                return ProcessJson(list);
            if (string.Equals(fileFormat, "csv", StringComparison.OrdinalIgnoreCase))
                return ProcessCsv(list);
            throw new Exception("Export format not supported");
        }

        private static string ProcessJson(List<ExportTransaction> invoices)
        {
            var serializerSett = new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };
            var json = JsonConvert.SerializeObject(invoices, Formatting.Indented, serializerSett);
            return json;
        }

        private static string ProcessCsv(IEnumerable<ExportTransaction> invoices)
        {
            using StringWriter writer = new();
            using var csvWriter = new CsvHelper.CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture), true);
            csvWriter.WriteHeader<ExportTransaction>();
            csvWriter.NextRecord();
            csvWriter.WriteRecords(invoices);
            csvWriter.Flush();
            return writer.ToString();
        }
    }

    public class ExportTransaction
    {
        public string Id { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public bool Positive { get; set; }
        public string Balance { get; set; }
        public bool IsConfirmed { get; set; }
        public string Comment { get; set; }
        public List<string> Labels { get; set; }
    }
}
