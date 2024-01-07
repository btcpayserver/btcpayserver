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

        public Color TextColor(Color bg)
        {
            int nThreshold = 105;
            int bgDelta = Convert.ToInt32(bg.R * 0.299 + bg.G * 0.587 + bg.B * 0.114);
            return 255 - bgDelta < nThreshold ? Color.Black : Color.White;
        }

        public string TextColor(string bg)
        {
            var color = TextColor(FromHtml(bg));
            return ColorTranslator.ToHtml(color).ToLowerInvariant();
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

        /// https://gist.github.com/zihotki/09fc41d52981fb6f93a81ebf20b35cd5
        /// <summary>
        /// Creates color with corrected brightness.
        /// </summary>
        /// <param name="color">Color to correct.</param>
        /// <param name="correctionFactor">The brightness correction factor. Must be between -1 and 1. 
        /// Negative values produce darker colors.</param>
        /// <returns>
        /// Corrected <see cref="Color"/> structure.
        /// </returns>
        public Color AdjustBrightness(Color color, float correctionFactor)
        {
            float red = color.R;
            float green = color.G;
            float blue = color.B;

            if (correctionFactor < 0)
            {
                correctionFactor = 1 + correctionFactor;
                red *= correctionFactor;
                green *= correctionFactor;
                blue *= correctionFactor;
            }
            else
            {
                red = (255 - red) * correctionFactor + red;
                green = (255 - green) * correctionFactor + green;
                blue = (255 - blue) * correctionFactor + blue;
            }

            return Color.FromArgb(color.A, (int)red, (int)green, (int)blue);
        }

        public string AdjustBrightness(string html, float correctionFactor)
        {
            var color = AdjustBrightness(ColorTranslator.FromHtml(html), correctionFactor);
            return ColorTranslator.ToHtml(color);
        }

        public Color FromHtml(string html)
        {
            return ColorTranslator.FromHtml(html);
        }
    }
}
