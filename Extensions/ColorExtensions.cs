using System;
using System.Drawing;

namespace BTCPayServer;

public static class ColorExtensions
{
    public static double GetLuminance(this Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        r = (r <= 0.03928) ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
        g = (g <= 0.03928) ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
        b = (b <= 0.03928) ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);

        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    public static double GetContrastRatio(this Color color1, Color color2)
    {
        var luminance1 = color1.GetLuminance();
        var luminance2 = color2.GetLuminance();
        var lighter = Math.Max(luminance1, luminance2);
        var darker = Math.Min(luminance1, luminance2);

        return (lighter + 0.05) / (darker + 0.05);
    }
    
    public static Color GetAdjustedForegroundForBackground(this Color foregroundColor, Color backgroundColor, double lightnessIncrement = 0.01, double threshold = 4.5)
    {
        var contrastRatio = foregroundColor.GetContrastRatio(backgroundColor);
        if (contrastRatio >= threshold) return foregroundColor;

        RgbToHsl(foregroundColor, out double hue, out double saturation, out double lightness);

        for (int i = 0; i < 100; i++)
        {
            lightness += lightnessIncrement;
            lightness = Math.Clamp(lightness, 0, 1);

            Color adjustedColor = HslToRgb(hue, saturation, lightness);
            contrastRatio = adjustedColor.GetContrastRatio(backgroundColor);

            if (contrastRatio >= threshold) return adjustedColor;
        }

        return foregroundColor;
    }
    
    private static void RgbToHsl(Color color, out double hue, out double saturation, out double lightness)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        lightness = (max + min) / 2.0;

        if (max == min)
        {
            hue = saturation = 0;
        }
        else
        {
            double delta = max - min;
            saturation = (lightness > 0.5) ? delta / (2.0 - max - min) : delta / (max + min);

            if (max == r)
            {
                hue = (g - b) / delta + (g < b ? 6 : 0);
            }
            else if (max == g)
            {
                hue = (b - r) / delta + 2;
            }
            else
            {
                hue = (r - g) / delta + 4;
            }

            hue /= 6;
        }
    }

    private static Color HslToRgb(double hue, double saturation, double lightness)
    {
        double r, g, b;

        if (saturation == 0)
        {
            r = g = b = lightness;
        }
        else
        {
            Func<double, double, double, double> hueToRgb = (p, q, t) =>
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1 / 6.0) return p + (q - p) * 6 * t;
                if (t < 1 / 2.0) return q;
                if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
                return p;
            };

            double q = (lightness < 0.5) ? lightness * (1 + saturation) : lightness + saturation - lightness * saturation;
            double p = 2 * lightness - q;

            r = hueToRgb(p, q, hue + 1 / 3.0);
            g = hueToRgb(p, q, hue);
            b = hueToRgb(p, q, hue - 1 / 3.0);
        }

        return Color.FromArgb(
            (int)Math.Round(r * 255),
            (int)Math.Round(g * 255),
            (int)Math.Round(b * 255));
    }
}
