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
using CsvHelper.Configuration.Attributes;
using NBXplorer.Models;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Wallets.Export
{
    public class TransactionsExport
    {
        private readonly BTCPayWallet _wallet;
        private readonly Dictionary<string, WalletTransactionInfo> _walletTransactionsInfo;

        public TransactionsExport(BTCPayWallet wallet, Dictionary<string, WalletTransactionInfo> walletTransactionsInfo)
        {
            _wallet = wallet;
            _walletTransactionsInfo = walletTransactionsInfo;
        }

        public string Process(IEnumerable<TransactionHistoryLine> inputList, string fileFormat)
        {
            var list = inputList.Select(tx =>
            {
                var model = new ExportTransaction
                {
                    TransactionId = tx.TransactionId.ToString(),
                    Timestamp = tx.SeenAt,
                    Amount = tx.BalanceChange.ShowMoney(_wallet.Network),
                    Currency = _wallet.Network.CryptoCode,
                    IsConfirmed = tx.Confirmations != 0
                };
                
                if (_walletTransactionsInfo.TryGetValue(tx.TransactionId.ToString(), out var transactionInfo))
                {
                    model.Labels = transactionInfo.LabelColors?.Select(l => l.Key).ToList();
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
            csvWriter.Configuration.RegisterClassMap<ExportTransactionMap>();
            csvWriter.WriteHeader<ExportTransaction>();
            csvWriter.NextRecord();
            csvWriter.WriteRecords(invoices);
            csvWriter.Flush();
            return writer.ToString();
        }
    }
    
    public sealed class ExportTransactionMap : ClassMap<ExportTransaction>
    {
        public ExportTransactionMap()
        {
            AutoMap(CultureInfo.InvariantCulture);
            Map(m => m.Labels).ConvertUsing(row => row.Labels == null ? string.Empty : string.Join(", ", row.Labels));
        }
    }

    public class ExportTransaction
    {
        [Name("Transaction Id")]
        public string TransactionId { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string Amount { get; set; }
        public string Currency { get; set; }
        
        [Name("Is Confirmed")]
        public bool IsConfirmed { get; set; }
        public string Comment { get; set; }
        public List<string> Labels { get; set; }
    }
}
