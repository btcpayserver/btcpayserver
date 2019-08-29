using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BTCPayServer.Data
{
    public class WalletData
    {
        [System.ComponentModel.DataAnnotations.Key]
        public string Id { get; set; }

        public List<WalletTransactionData> WalletTransactions { get; set; }

        public byte[] Blob { get; set; }
    }

    public class Label
    {
        public Label(string value, string color)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (color == null)
                throw new ArgumentNullException(nameof(color));
            Value = value;
            Color = color;
        }

        public string Value { get; }
        public string Color { get; }

        public override bool Equals(object obj)
        {
            Label item = obj as Label;
            if (item == null)
                return false;
            return Value.Equals(item.Value, StringComparison.OrdinalIgnoreCase);
        }
        public static bool operator ==(Label a, Label b)
        {
            if (System.Object.ReferenceEquals(a, b))
                return true;
            if (((object)a == null) || ((object)b == null))
                return false;
            return a.Value == b.Value;
        }

        public static bool operator !=(Label a, Label b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode(StringComparison.OrdinalIgnoreCase);
        }
    }

    public class WalletBlobInfo
    {
        public Dictionary<string, string> LabelColors { get; set; } = new Dictionary<string, string>();

        public IEnumerable<Label> GetLabels(WalletTransactionInfo transactionInfo)
        {
            foreach (var label in transactionInfo.Labels)
            {
                if (LabelColors.TryGetValue(label, out var color))
                {
                    yield return new Label(label, color);
                }
            }
        }

        public IEnumerable<Label> GetLabels()
        {
            foreach (var kv in LabelColors)
            {
                yield return new Label(kv.Key, kv.Value);
            }
        }
    }
}
