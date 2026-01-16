using System;
using System.Reflection;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Models.PaymentRequestViewModels;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Models.WalletViewModels;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace BTCPayServer
{
    // Classes here remember users preferences on certain pages and store them in unified blob cookie "UserPrefsCookie"
    public static class ControllerBaseExtension
    {
        public static T ParseListQuery<T>(this ControllerBase ctrl, T model) where T : BasePagingViewModel
        {
            PropertyInfo prop;
            if (model is InvoicesModel)
                prop = typeof(UserPrefsCookie).GetProperty(nameof(UserPrefsCookie.InvoicesQuery));
            else if (model is ListPaymentRequestsViewModel)
                prop = typeof(UserPrefsCookie).GetProperty(nameof(UserPrefsCookie.PaymentRequestsQuery));
            else if (model is UsersViewModel)
                prop = typeof(UserPrefsCookie).GetProperty(nameof(UserPrefsCookie.UsersQuery));
            else if (model is PayoutsModel)
                prop = typeof(UserPrefsCookie).GetProperty(nameof(UserPrefsCookie.PayoutsQuery));
            else if (model is PullPaymentsModel)
                prop = typeof(UserPrefsCookie).GetProperty(nameof(UserPrefsCookie.PullPaymentsQuery));
            else
                throw new Exception("Unsupported BasePagingViewModel for cookie user preferences saving");

            return ProcessParse(ctrl, model, prop);
        }

        private static T ProcessParse<T>(ControllerBase ctrl, T model, PropertyInfo prop) where T : BasePagingViewModel
        {
            var prefCookie = ctrl.HttpContext.GetUserPrefsCookie();

            // If the user enter an empty searchTerm, then the variable will be null and not empty string
            // but we want searchTerm to be null only if the user is browsing the page via some link
            // NOT if the user entered some empty search
            var searchTerm = model.SearchTerm;
            searchTerm = searchTerm is not null ? searchTerm :
                         ctrl.Request.Query.ContainsKey(nameof(searchTerm)) ? string.Empty :
                         null;
            if (searchTerm is null)
            {
                var section = prop.GetValue(prefCookie) as ListQueryDataHolder;
                if (section != null && !string.IsNullOrEmpty(section.SearchTerm))
                {
                    model.SearchTerm = section.SearchTerm;
                    model.TimezoneOffset = section.TimezoneOffset ?? 0;
                    model.Count = section.Count ?? BasePagingViewModel.CountDefault;
                }
            }
            else
            {
                prop.SetValue(prefCookie, new ListQueryDataHolder(model.SearchTerm, model.TimezoneOffset, model.Count));
                ctrl.Response.Cookies.Append(nameof(UserPrefsCookie), JsonConvert.SerializeObject(prefCookie));
            }

            return model;
        }
    }
}
