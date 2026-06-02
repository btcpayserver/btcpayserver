using System.Collections.Generic;
using BTCPayServer.Plugins.Emails.Views;

namespace BTCPayServer.Plugins.Multisig;

public static class MultisigEmailTriggers
{
    public const string SignerKeyRequested = "MultisigSignerKeyRequested";
    public const string SignerKeySubmitted = "MultisigSignerKeySubmitted";
    public const string WalletCreated = "MultisigWalletCreated";
    public const string PendingTransactionCreated = "MultisigPendingTransactionCreated";
    public const string PendingTransactionSignatureCollected = "MultisigPendingTransactionSignatureCollected";

    public static readonly string[] DefaultRuleTriggers =
    [
        SignerKeyRequested,
        SignerKeySubmitted,
        WalletCreated,
        PendingTransactionCreated,
        PendingTransactionSignatureCollected
    ];

    public static IEnumerable<EmailTriggerViewModel> GetViewModels()
    {
        var signerPlaceholders = new List<EmailTriggerViewModel.PlaceHolder>
        {
            new("{CryptoCode}", "The wallet crypto code"),
            new("{Request.Id}", "The multisig setup request id"),
            new("{Signer.Email}", "The signer email address"),
            new("{Signer.Name}", "The signer name"),
            new("{Signer.Link}", "The multisig setup session link")
        };

        yield return new EmailTriggerViewModel
        {
            Trigger = SignerKeyRequested,
            Description = "Multisig - Signer Key Requested",
            DefaultEmail = new()
            {
                To = ["{Signer.Email}"],
                Subject = "Multisig signer request for {CryptoCode}",
                Body = "A multisig wallet setup requires your account key.<br/>Open this link and submit your signer key:<br/><a href=\"{Signer.Link}\">{Signer.Link}</a>"
            },
            PlaceHolders = signerPlaceholders
        };

        yield return new EmailTriggerViewModel
        {
            Trigger = SignerKeySubmitted,
            Description = "Multisig - Signer Key Submitted",
            DefaultEmail = new()
            {
                To = ["{Recipient.Email}"],
                Subject = "Multisig signer submitted ({CryptoCode})",
                Body = "<b>{Signer.Name}</b> submitted a signer key for request <span class=\"font-monospace\">{Request.Id}</span>.<br/>Open request: <a href=\"{Request.Link}\">{Request.Link}</a>"
            },
            PlaceHolders = new()
            {
                new("{CryptoCode}", "The wallet crypto code"),
                new("{Request.Id}", "The multisig setup request id"),
                new("{Request.Link}", "The multisig setup link"),
                new("{Signer.Email}", "The signer email address"),
                new("{Signer.Name}", "The signer name"),
                new("{Recipient.Email}", "The recipient email address")
            }
        };

        yield return new EmailTriggerViewModel
        {
            Trigger = WalletCreated,
            Description = "Multisig - Wallet Created",
            DefaultEmail = new()
            {
                To = ["{Recipient.Email}"],
                Subject = "Multisig wallet created for {CryptoCode}",
                Body = "The multisig wallet setup is complete.<br/>Open wallet: <a href=\"{Wallet.Link}\">{Wallet.Link}</a>"
            },
            PlaceHolders = new()
            {
                new("{CryptoCode}", "The wallet crypto code"),
                new("{Request.Id}", "The multisig setup request id"),
                new("{Wallet.Link}", "The wallet link"),
                new("{Recipient.Email}", "The recipient email address")
            }
        };

        var pendingPlaceholders = new List<EmailTriggerViewModel.PlaceHolder>
        {
            new("{CryptoCode}", "The wallet crypto code"),
            new("{PendingTransaction.Link}", "The pending transaction link"),
            new("{PendingTransaction.SignaturesCollected}", "The number of collected signatures"),
            new("{PendingTransaction.SignaturesNeeded}", "The number of required signatures"),
            new("{PendingTransaction.SignaturesMissing}", "The number of missing signatures"),
            new("{Recipient.Email}", "The recipient email address")
        };

        yield return new EmailTriggerViewModel
        {
            Trigger = PendingTransactionCreated,
            Description = "Multisig - Pending Transaction Created",
            DefaultEmail = new()
            {
                To = ["{Recipient.Email}"],
                Subject = "Pending multisig transaction requires signatures ({CryptoCode})",
                Body = "A pending multisig transaction was created and needs signatures.<br/><a href=\"{PendingTransaction.Link}\">Open pending transaction</a>"
            },
            PlaceHolders = pendingPlaceholders
        };

        yield return new EmailTriggerViewModel
        {
            Trigger = PendingTransactionSignatureCollected,
            Description = "Multisig - Pending Transaction Signature Collected",
            DefaultEmail = new()
            {
                To = ["{Recipient.Email}"],
                Subject = "Multisig signature collected ({CryptoCode})",
                Body = "A signer submitted a signature for the pending multisig transaction.<br/>Progress: <b>{PendingTransaction.SignaturesCollected}/{PendingTransaction.SignaturesNeeded}</b> collected, {PendingTransaction.SignaturesMissing} missing.<br/><a href=\"{PendingTransaction.Link}\">Open pending transaction</a>"
            },
            PlaceHolders = pendingPlaceholders
        };
    }
}
