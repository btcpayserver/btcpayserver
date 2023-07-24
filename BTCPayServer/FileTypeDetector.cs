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
            "AVIF image file, XX XX XX XX 66 74 79 70,AVIF,Picture,0,(null)\n" +
            "MP3 audio file,49 44 33,MP3,Multimedia,0,(null)\n" +
            "MP3 audio file,FF,MP3,Multimedia,0,(null)\n" +
            "RIFF Windows Audio,57 41 56 45 66 6D 74 20,WAV,Multimedia,8,(null)\n" +
            "Free Lossless Audio Codec file,66 4C 61 43 00 00 00 22,FLAC,Multimedia,0,(null)\n" +
            "MPEG-4 AAC audio,FF F1,AAC,Audio,0,(null)\n" +
            "Ogg Vorbis Codec compressed file,4F 67 67 53,OGA|OGG|OGV|OGX,Multimedia,0,(null)\n" +
            "Apple Lossless Audio Codec file,66 74 79 70 4D 34 41 20,M4A,Multimedia,4,(null)\n" +
            "WebM/WebA,66 74 79 70 4D 34 41 20,M4A,Multimedia,4,(null)\n" +
            "WebM/WEBA video file,1A 45 DF A3,WEBM|WEBA,Multimedia,0,(null)\n" +
            "Resource Interchange File Format,52 49 46 46,AVI|CDA|QCP|RMI|WAV|WEBP,Multimedia,0,(null)\n";

        readonly static (int[] Header, int[]? Trailer, string Type, string[] Extensions)[] headerTrailers;
        static FileTypeDetector()
        {
            var lines = pictureSigs.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            headerTrailers = new (int[] Header, int[]? Trailer, string Type, string[] Extensions)[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                var cells = lines[i].Split(',');
                headerTrailers[i] = (
                    DecodeData(cells[1]),
                    cells[^1] == "(null)" ? null : DecodeData(cells[^1]),
                    cells[3],
                    cells[2].Split('|').Select(p => $".{p}").ToArray()
                    );
            }
        }

        private static int[] DecodeData(string pattern)
        {
            pattern = pattern.Replace(" ", "");
            int[] res = new int[pattern.Length / 2];
            for (int i = 0; i < pattern.Length; i += 2)
            {
                var b = pattern[i..(i + 2)];
                if (b == "XX")
                    res[i / 2] = -1;
                else
                    res[i / 2] = byte.Parse(b, System.Globalization.NumberStyles.HexNumber);
            }
            return res;
        }
        public static bool IsPicture(byte[] bytes, string? filename)
        {
            return IsFileType(bytes, filename, new[] { "Picture" });
        }
        public static bool IsAudio(byte[] bytes, string? filename)
        {
            return IsFileType(bytes, filename, new[] { "Multimedia", "Audio" });
        }

        static bool IsFileType(byte[] bytes, string? filename, string[] types)
        {
            for (int i = 0; i < headerTrailers.Length; i++)
            {
                if (!types.Contains(headerTrailers[i].Type))
                    goto next;
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
                        goto next;
                }
                return true;
next:
                ;
            }
            return false;
        }
    }
}
