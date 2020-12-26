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
            SvgQRCode qrCode = new SvgQRCode(qrCodeData);
            return new HtmlContentViewComponentResult(new HtmlString(qrCode.GetGraphic(new Size(256,256), "#000", "#f5f5f7", true, SvgQRCode.SizingMode.ViewBoxAttribute)));
        }
    }
}
