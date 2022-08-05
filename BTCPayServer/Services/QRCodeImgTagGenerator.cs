using System;
using QRCoder;

namespace BTCPayServer.Services;

public class QRCodeImgTagGenerator
{
    private static QRCodeGenerator qrGenerator = new();
    
    public string generateImgTag(string data)
    {
        QRCodeData qrCodeData = qrGenerator.CreateQrCode(data, QRCodeGenerator.ECCLevel.Q);
        PngByteQRCode qrCode = new(qrCodeData);
        var bytes = qrCode.GetGraphic(5, new byte[] { 0, 0, 0, 255 }, new byte[] { 0xf5, 0xf5, 0xf7, 255 }, true);
        var b64 = Convert.ToBase64String(bytes);
        return $"<img height=\"256\" style=\"image-rendering: pixelated;image-rendering: -moz-crisp-edges;\" src=\"data:image/png;base64,{b64}\" />";
    }
    
}
