using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Services.Notifications.Blobs;

internal class NewUserRequiresApprovalNotification : BaseNotification
{
    private const string TYPE = "newuserrequiresapproval";
    public string UserId { get; set; }
    public string UserEmail { get; set; }
    public override string Identifier => TYPE;
    public override string NotificationType => TYPE;

    public NewUserRequiresApprovalNotification()
    {
    }

    public NewUserRequiresApprovalNotification(ApplicationUser user)
    {
        UserId = user.Id;
        UserEmail = user.Email;
    }

    internal class Handler : NotificationHandler<NewUserRequiresApprovalNotification>
    {
        private readonly LinkGenerator _linkGenerator;
        private readonly BTCPayServerOptions _options;
        private IStringLocalizer StringLocalizer { get; }

        public Handler(LinkGenerator linkGenerator, BTCPayServerOptions options, IStringLocalizer stringLocalizer)
        {
            _linkGenerator = linkGenerator;
            _options = options;
            StringLocalizer = stringLocalizer;
        }

        public override string NotificationType => TYPE;
        public override (string identifier, string name)[] Meta
        {
            get
            {
                return [(TYPE, StringLocalizer["New user requires approval"])];
            }
        }

        protected override void FillViewModel(NewUserRequiresApprovalNotification notification, NotificationViewModel vm)
        {
            vm.Identifier = notification.Identifier;
            vm.Type = notification.NotificationType;
            vm.Body = StringLocalizer["New user {0} requires approval.", notification.UserEmail];
            vm.ActionLink = _linkGenerator.GetPathByAction(nameof(UIServerController.User),
                "UIServer",
                new { userId = notification.UserId }, _options.RootPath);
        }
    }
}
