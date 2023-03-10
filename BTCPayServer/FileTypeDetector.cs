#nullable enable
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using NBitcoin.DataEncoders;

namespace BTCPayServer
{
    public class FileTypeDetector
    {
        // Thanks to https://www.garykessler.net/software/FileSigs_20220731.zip

        const string pictureSigs =
            "JPEG2000 image files,00 00 00 0C 6A 50 20 20,JP2,Picture,0,(null)\n" +
            "Bitmap image,42 4D,BMP|DIB,Picture,0,(null)\n" +
            "GIF file,47 49 46 38,GIF,Picture,0,00 3B\n" +
            "PNG image,89 50 4E 47 0D 0A 1A 0A,PNG|APNG,Picture,0,49 45 4E 44 AE 42 60 82\n" +
            "Generic JPEGimage fil,FF D8,JPE|JPEG|JPG,Picture,0,FF D9\n" +
            "JPEG-EXIF-SPIFF images,FF D8 FF,JFIF|JPE|JPEG|JPG,Picture,0,FF D9\n" +
            "SVG images, 3C 73 76 67,SVG,Picture,0,(null)\n" +
            "Google WebP image file, 52 49 46 46 XX XX XX XX 57 45 42 50,WEBP,Picture,0,(null)\n" +
            "AVIF image file, XX XX XX XX 66 74 79 70,AVIF,Picture,0,(null)\n";

        readonly static (int[] Header, int[]? Trailer, string[] Extensions)[] headerTrailers;
        static FileTypeDetector()
        {
            var lines = pictureSigs.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            headerTrailers = new (int[] Header, int[]? Trailer, string[] Extensions)[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                var cells = lines[i].Split(',');
                headerTrailers[i] = (
                    DecodeData(cells[1]),
                    cells[^1] == "(null)" ? null : DecodeData(cells[^1]),
                    cells[2].Split('|').Select(p => $".{p}").ToArray()
                    );
            }
        }

        private static int[] DecodeData(string pattern)
        {
            pattern = pattern.Replace(" ", "");
            int[] res = new int[pattern.Length / 2];
            for (int i = 0; i < pattern.Length; i+=2)
            {
                var b = pattern[i..(i + 2)];
                if (b == "XX")
                    res[i/2] = -1;
                else
                    res[i/2] = byte.Parse(b, System.Globalization.NumberStyles.HexNumber);
            }
            return res;
        }

        public static bool IsPicture(byte[] bytes, string? filename)
        {
            for (int i = 0; i < headerTrailers.Length; i++)
            {
                if (headerTrailers[i].Header is int[] header)
                {
                    if (header.Length > bytes.Length)
                        goto next;
                    for (int x = 0; x < header.Length; x++)
                    {
                        if (bytes[x] != header[x] && header[x] != -1)
                            goto next;
                    }
                }
                if (headerTrailers[i].Trailer is int[] trailer)
                {
                    if (trailer.Length > bytes.Length)
                        goto next;
                    for (int x = 0; x < trailer.Length; x++)
                    {
                        if (bytes[^(trailer.Length - x)] != trailer[x] && trailer[x] != -1)
                            goto next;
                    }
                }

                if (filename is not null)
                {
                    if (!headerTrailers[i].Extensions.Any(ext => filename.Length > ext.Length && filename.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                        return false;
                }
                return true;
next:
                ;
            }
            return false;
        }
    }
}
