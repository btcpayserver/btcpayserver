using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QRCoder;

namespace BTCPayServer.Plugins.LNbank.Controllers
{
    public sealed class QrcodeController : Controller
    {
        [AllowAnonymous]
        [HttpGet("~/QR/{encode}")]
        public IActionResult Details(string encode)
        {
            QRCodeGenerator qrGenerator = new QRCodeGenerator();
            QRCodeData qrCodeData = qrGenerator.CreateQrCode(encode, QRCodeGenerator.ECCLevel.Q);
            SvgQRCode qrCode = new SvgQRCode(qrCodeData);
            string qrCodeAsSvg = qrCode.GetGraphic(5);
            return Content(qrCodeAsSvg, "image/svg+xml");
        }
    }
}
