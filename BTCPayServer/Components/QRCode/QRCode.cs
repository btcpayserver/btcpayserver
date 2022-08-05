using System;
using System.Drawing;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;

namespace BTCPayServer.Components.QRCode
{
    public class QRCode : ViewComponent
    {
        private readonly QRCodeImgTagGenerator _imgTagGenerator;

        public QRCode(QRCodeImgTagGenerator imgTagGenerator)
        {
            _imgTagGenerator = imgTagGenerator;
        }


        public IViewComponentResult Invoke(string data)
        {
            var html = _imgTagGenerator.generateImgTag(data);
            return new HtmlContentViewComponentResult(new HtmlString(html));
        }
    }
}
