#nullable enable
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Views.UIStoreMembership;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers;

[Authorize(Policy = Policies.CanViewMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
public class UIStoreMembershipController : Controller
{
    [HttpGet("stores/{storeId}/membership/{section=Plans}")]
    public IActionResult Membership(MembershipSection section = MembershipSection.Plans)
    {
        return View(new MembershipViewModel(){ Section = section });
    }

    [HttpGet("stores/{storeId}/membership/{section=Plans}/add-plan")]
    [Authorize(Policy = Policies.CanModifyMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult AddMembershipPlan()
    {
        return View(new AddEditMembershipPlanViewModel());
    }
}
