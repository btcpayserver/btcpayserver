using System;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using NBitcoin.Crypto;

namespace BTCPayServer
{
    public class ColorPalette
    {
        public const string Pattern = "^#[0-9a-fA-F]{6}$";
        public static bool IsValid(string color)
        {
            return Regex.Match(color, Pattern).Success;
        }
        public string TextColor(string bgColor)
        {
            int nThreshold = 105;
            var bg = ColorTranslator.FromHtml(bgColor);
            int bgDelta = Convert.ToInt32((bg.R * 0.299) + (bg.G * 0.587) + (bg.B * 0.114));
            Color color = (255 - bgDelta < nThreshold) ? Color.Black : Color.White;
            return ColorTranslator.ToHtml(color);
        }
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
            switch (label)
            {
                case "payjoin":
                    return "#51b13e";
                case "invoice":
                    return "#cedc21";
                case "payment-request":
                    return "#489D77";
                case "app":
                    return "#5093B6";
                case "pj-exposed":
                    return "#51b13e";
                case "payout":
                    return "#3F88AF";
                default:
                    var num = NBitcoin.Utils.ToUInt32(Hashes.SHA256(Encoding.UTF8.GetBytes(label)), 0, true);
                    return Labels[num % Labels.Length];
            }
        }
    }
}
