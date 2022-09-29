using System.Text;
using NBitcoin.Crypto;

namespace BTCPayServer
{
    public class ColorPalette
    {
        // Borrowed from https://github.com/ManageIQ/guides/blob/master/labels.md
        public static readonly ColorPalette Default = new ColorPalette(new string[] {
            "#fbca04",
            "#0e8a16",
            "#ff7619",
            "#84b6eb",
            "#5319e7",
            "#cdcdcd",
            "#cc317c",
        });
        private ColorPalette(string[] labels)
        {
            Labels = labels;
        }
        
        public readonly string[] Labels;

        public string DeterministicColor(string label)
        {
            var num = NBitcoin.Utils.ToUInt32(Hashes.SHA256(Encoding.UTF8.GetBytes(label)), 0, true);
            return Labels[num % Labels.Length];
        }
    }
}
