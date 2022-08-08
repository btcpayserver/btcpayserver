using System;
using System.Drawing;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using QRCoder;

namespace BTCPayServer.Components.QRCode
{
    public class QRCode : ViewComponent
    {
        private static QRCodeGenerator qrGenerator = new QRCodeGenerator();


        public IViewComponentResult Invoke(string data)
        {
            QRCodeData qrCodeData = qrGenerator.CreateQrCode(data, QRCodeGenerator.ECCLevel.Q);
            PngByteQRCode qrCode = new PngByteQRCode(qrCodeData);
            var bytes = qrCode.GetGraphic(5, new byte[] { 0, 0, 0, 255 }, new byte[] { 0xf5, 0xf5, 0xf7, 255 }, true);
            var b64 = Convert.ToBase64String(bytes);
            return new HtmlContentViewComponentResult(new HtmlString($"<img height=\"256\" style=\"image-rendering: pixelated;image-rendering: -moz-crisp-edges;\" src=\"data:image/png;base64,{b64}\" />"));
        }
    }
}
