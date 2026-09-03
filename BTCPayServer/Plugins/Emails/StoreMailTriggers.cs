using System.Collections.Generic;
using BTCPayServer.Plugins.Emails.Views;

namespace BTCPayServer.Plugins.Emails;

public static class StoreMailTriggers
{
    public const string StoreInvitePending = "StoreInvitePending";
    public const string UserJoined = "StoreUserJoined";

    public static IEnumerable<EmailTriggerViewModel> GetViewModels()
    {
        yield return new EmailTriggerViewModel
        {
            Trigger = StoreInvitePending,
            Description = "User: Store invitation",
            DefaultEmail = new EmailTriggerViewModel.Default
            {
                To = ["{User.MailboxAddress}"],
                Subject = "Invitation to join {StoreInvitation.StoreName}",
                Body = EmailsPlugin.CreateEmail("You have been invited to join <b>{StoreInvitation.StoreName}</b> as {StoreInvitation.Role}.", "View invitation", "{StoreInvitation.Link}")
            },
            PlaceHolders =
            [
                new("{User.Name}", "The name of the user (eg. John Doe)"),
                new("{User.Email}", "The email of the user (eg. john.doe@example.com)"),
                new("{User.MailboxAddress}", "The formatted mailbox address to use when sending an email. (eg. \"John Doe\" <john.doe@example.com>)"),
                new("{StoreInvitation.StoreName}", "The name of the store the user is invited to"),
                new("{StoreInvitation.Role}", "The role the user is invited with"),
                new("{StoreInvitation.Link}", "The link where the user can accept or decline the invitation")
            ]
        };

        yield return new EmailTriggerViewModel
        {
            Trigger = UserJoined,
            Description = "Store: User joined",
            DefaultEmail = new EmailTriggerViewModel.Default
            {
                To = ["{JoinedUser.MailboxAddress}"],
                Subject = "Welcome to {Store.Name}",
                Body = EmailsPlugin.CreateEmail("You now have access to {Store.Name} with the role {JoinedUser.Role}.")
            },
            PlaceHolders =
            [
                new("{JoinedUser.Name}", "The name of the user who joined the store"),
                new("{JoinedUser.Email}", "The email of the user who joined the store"),
                new("{JoinedUser.MailboxAddress}", "The formatted mailbox address of the user who joined the store"),
                new("{JoinedUser.Role}", "The role the user joined the store with")
            ]
        };
    }
}
