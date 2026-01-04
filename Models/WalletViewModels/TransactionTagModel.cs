using System;

namespace BTCPayServer.Models.WalletViewModels
{
    public class TransactionTagModel
    {
        public string Text { get; internal set; }
        public string Color { get; internal set; }
        public string TextColor { get; internal set; }
        public string Link { get; internal set; }
        public string Tooltip { get; internal set; } = String.Empty;

        public override bool Equals(object obj)
        {
            TransactionTagModel item = obj as TransactionTagModel;
            if (item == null)
                return false;
            return Text.Equals(item.Text, StringComparison.OrdinalIgnoreCase);
        }

        public static bool operator ==(TransactionTagModel a, TransactionTagModel b)
        {
            if (System.Object.ReferenceEquals(a, b))
                return true;
            if (((object)a == null) || ((object)b == null))
                return false;
            return a.Text == b.Text;
        }

        public static bool operator !=(TransactionTagModel a, TransactionTagModel b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return Text.GetHashCode(StringComparison.OrdinalIgnoreCase);
        }
    }
}
