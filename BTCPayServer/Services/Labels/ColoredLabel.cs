using System;

namespace BTCPayServer.Services.Labels
{
    public class ColoredLabel
    {
        internal ColoredLabel()
        {
        }

        public string Text { get; internal set; }
        public string Color { get; internal set; }
        public string Link { get; internal set; }
        public string Tooltip { get; internal set; }

        public override bool Equals(object obj)
        {
            ColoredLabel item = obj as ColoredLabel;
            if (item == null)
                return false;
            return Text.Equals(item.Text, StringComparison.OrdinalIgnoreCase);
        }

        public static bool operator ==(ColoredLabel a, ColoredLabel b)
        {
            if (System.Object.ReferenceEquals(a, b))
                return true;
            if (((object)a == null) || ((object)b == null))
                return false;
            return a.Text == b.Text;
        }

        public static bool operator !=(ColoredLabel a, ColoredLabel b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return Text.GetHashCode(StringComparison.OrdinalIgnoreCase);
        }
    }
}
