﻿using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Client.Models
{
    public class ApprovePayoutRequest
    {
        public int Revision { get; set; }
        public string RateRule { get; set; }
    }
}
