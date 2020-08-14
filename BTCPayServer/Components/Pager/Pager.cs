using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components
{
    public class Pager : ViewComponent
    {
        public Pager()
        {
        }
        public IViewComponentResult Invoke(BasePagingViewModel viewModel)
        {
            return View(viewModel);
        }
    }
}
