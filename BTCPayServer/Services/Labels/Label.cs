using System;

namespace BTCPayServer.Services.Labels
{
    public class Label
    {
        internal Label()
        {
        }

        public string Value { get; internal set; }
        public string RawValue { get; internal set; }
        public string Color { get; internal set; }
        public string Link { get; internal set; }
        public string Tooltip { get; internal set; }

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
}
