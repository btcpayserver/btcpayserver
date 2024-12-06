using System;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using QRCoder;

namespace BTCPayServer.Components.QRCode
{
    public class QRCode : ViewComponent
    {
        private static QRCodeGenerator _qrGenerator = new();

        public IViewComponentResult Invoke(string data, int size=256)
        {
            var qrCodeData = _qrGenerator.CreateQrCode(data, QRCodeGenerator.ECCLevel.Q);
            var qrCode = new PngByteQRCode(qrCodeData);
            var bytes = qrCode.GetGraphic(5, new byte[] { 0, 0, 0, 255 }, new byte[] { 0xf5, 0xf5, 0xf7, 255 });
            var b64 = Convert.ToBase64String(bytes);
            return new HtmlContentViewComponentResult(new HtmlString(
                $"<img style=\"image-rendering:pixelated;image-rendering:-moz-crisp-edges;min-width:{size}px;min-height:{size}px\" src=\"data:image/png;base64,{b64}\" class=\"qr-code\" />"));
        }
    }
}
