using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Security;
using BTCPayServer.Services.GlobalSearch;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Controllers
{
    [Route("search")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class UISearchController : Controller
    {
        private const int MaxQueryLength = 200;
        private const string ILikeEscapeCharacter = "\\";

        private readonly StoreRepository _storeRepository;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuthorizationService _authorizationService;
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly IEnumerable<IGlobalSearchProvider> _globalSearchProviders;
        private readonly ILogger<UISearchController> _logger;

        public UISearchController(
            StoreRepository storeRepository,
            InvoiceRepository invoiceRepository,
            UserManager<ApplicationUser> userManager,
            IAuthorizationService authorizationService,
            ApplicationDbContextFactory dbContextFactory,
            IEnumerable<IGlobalSearchProvider> globalSearchProviders,
            ILogger<UISearchController> logger)
        {
            _storeRepository = storeRepository;
            _invoiceRepository = invoiceRepository;
            _userManager = userManager;
            _authorizationService = authorizationService;
            _dbContextFactory = dbContextFactory;
            _globalSearchProviders = globalSearchProviders;
            _logger = logger;
        }

        [HttpGet("global")]
        public async Task<IActionResult> Global(string q = null, string storeId = null, int take = 25)
        {
            var query = NormalizeQuery(q);
            take = Math.Clamp(take, 1, 50);

            var context = await BuildSearchContext(storeId);
            var parsedQuery = ParseQuery(query);

            var results = new List<GlobalSearchResult>(take * 2);

            if (!string.IsNullOrEmpty(parsedQuery.RawQuery))
            {
                await AddInvoiceResults(results, parsedQuery, take, context);
                await AddPaymentRequestResults(results, parsedQuery, take, context);
                await AddTransactionResults(results, parsedQuery.TransactionSearch, take, context);
            }

            results.AddRange(await BuildPageResults(context.Store, parsedQuery.RawQuery));
            // Let plugin providers contribute results without modifying this controller.
            await AddPluginResults(results, parsedQuery, storeId, take, context);

            var deduped = DeduplicateResults(results, take);

            return Json(deduped);
        }

        private async Task<SearchContext> BuildSearchContext(string storeId)
        {
            var userId = _userManager.GetUserId(User);
            var isServerAdmin = User.IsInRole(Roles.ServerAdmin);
            var store = await ResolveStore(storeId, userId, isServerAdmin);
            return new SearchContext(userId, isServerAdmin, store);
        }

        private static string NormalizeQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            query = query.Trim();
            if (query.Length <= MaxQueryLength)
                return query;

            return query[..MaxQueryLength];
        }

        private static GlobalSearchQuery ParseQuery(string query)
        {
            var dateRange = GetDateRangeSearch(query);
            return new GlobalSearchQuery
            {
                RawQuery = query,
                TransactionSearch = GetTransactionSearch(query),
                DateRange = dateRange,
                MatchQuery = dateRange.HasValue ? string.Empty : query
            };
        }

        private async Task AddPluginResults(
            IList<GlobalSearchResult> results,
            GlobalSearchQuery parsedQuery,
            string storeId,
            int take,
            SearchContext context)
        {
            if (_globalSearchProviders == null)
                return;

            var pluginContext = new GlobalSearchPluginContext(
                requestedStoreId: storeId,
                take: take,
                user: User,
                userId: context.UserId,
                isServerAdmin: context.IsServerAdmin,
                store: context.Store,
                query: parsedQuery,
                results: results);

            foreach (var provider in _globalSearchProviders)
            {
                try
                {
                    await provider.Search(pluginContext);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Global search provider {Provider} failed", provider.GetType().FullName);
                }
            }
        }

        private static GlobalSearchResult[] DeduplicateResults(IEnumerable<GlobalSearchResult> results, int take)
        {
            return results
                .Where(r => !string.IsNullOrEmpty(r.Url))
                .DistinctBy(r => $"{r.Category}|{r.Url}|{r.Title}", StringComparer.OrdinalIgnoreCase)
                .Take(take)
                .ToArray();
        }

        private async Task<StoreData> ResolveStore(string storeId, string userId, bool isServerAdmin)
        {
            if (string.IsNullOrWhiteSpace(storeId))
                return null;

            return isServerAdmin
                ? await _storeRepository.FindStore(storeId)
                : await _storeRepository.FindStore(storeId, userId);
        }

        private async Task AddInvoiceResults(
            ICollection<GlobalSearchResult> results,
            GlobalSearchQuery parsedQuery,
            int take,
            SearchContext context)
        {
            var storeIds = await GetStoreIdsWithPermission(context, Policies.CanViewInvoices);
            if (!context.IsServerAdmin && (storeIds == null || storeIds.Length == 0))
                return;

            var invoiceQuery = new InvoiceQuery
            {
                TextSearch = parsedQuery.DateRange.HasValue ? null : parsedQuery.RawQuery,
                StoreId = storeIds,
                UserId = context.IsServerAdmin ? null : context.UserId,
                IncludeArchived = true,
                Take = Math.Min(take, 10)
            };

            if (parsedQuery.DateRange.HasValue)
            {
                invoiceQuery.StartDate = parsedQuery.DateRange.Value.Start;
                invoiceQuery.EndDate = parsedQuery.DateRange.Value.End;
            }

            var invoices = await _invoiceRepository.GetInvoices(invoiceQuery);

            foreach (var invoice in invoices)
            {
                var amount = invoice.Price.ToString(CultureInfo.InvariantCulture);
                var orderId = invoice.Metadata?.OrderId;
                var subtitle = string.IsNullOrEmpty(orderId)
                    ? $"{amount} {invoice.Currency} · {invoice.GetInvoiceState()}"
                    : $"{amount} {invoice.Currency} · {invoice.GetInvoiceState()} · {orderId}";
                var url = Url.Action(nameof(UIInvoiceController.Invoice), "UIInvoice", new { storeId = invoice.StoreId, invoiceId = invoice.Id });

                AddMatch(results, parsedQuery.MatchQuery, new GlobalSearchResult
                {
                    Category = "Invoice",
                    Title = invoice.Id,
                    Subtitle = subtitle,
                    Url = url,
                    Keywords = orderId
                });
            }
        }

        private async Task AddPaymentRequestResults(
            ICollection<GlobalSearchResult> results,
            GlobalSearchQuery parsedQuery,
            int take,
            SearchContext context)
        {
            var storeIds = await GetStoreIdsWithPermission(context, Policies.CanViewPaymentRequests);
            if (!context.IsServerAdmin && (storeIds == null || storeIds.Length == 0))
                return;

            await using var dbContext = _dbContextFactory.CreateContext();
            IQueryable<PaymentRequestData> requestQuery = dbContext.PaymentRequests
                .Include(data => data.StoreData)
                .AsNoTracking();

            if (storeIds is { Length: > 0 })
                requestQuery = requestQuery.Where(data => storeIds.Contains(data.StoreDataId));

            if (parsedQuery.DateRange.HasValue)
            {
                requestQuery = requestQuery.Where(data =>
                    data.Created >= parsedQuery.DateRange.Value.Start && data.Created < parsedQuery.DateRange.Value.End);
            }
            else
            {
                var likePattern = CreateContainsILikePattern(parsedQuery.RawQuery);
                var amountOrNull = decimal.TryParse(parsedQuery.RawQuery, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
                    ? amount
                    : (decimal?)null;

                requestQuery = requestQuery.Where(data =>
                    data.ReferenceId == parsedQuery.RawQuery ||
                    data.Id == parsedQuery.RawQuery ||
                    EF.Functions.ILike(data.Title, likePattern, ILikeEscapeCharacter) ||
                    (amountOrNull.HasValue && data.Amount == amountOrNull.Value));
            }

            var paymentRequests = await requestQuery
                .OrderByDescending(data => data.Created)
                .Take(Math.Min(take, 8))
                .ToArrayAsync();

            foreach (var paymentRequest in paymentRequests)
            {
                var subtitle = string.IsNullOrEmpty(paymentRequest.StoreData?.StoreName)
                    ? $"{paymentRequest.Amount.ToString(CultureInfo.InvariantCulture)} {paymentRequest.Currency}"
                    : $"{paymentRequest.Amount.ToString(CultureInfo.InvariantCulture)} {paymentRequest.Currency} · {paymentRequest.StoreData.StoreName}";
                var url = Url.Action(nameof(UIPaymentRequestController.ViewPaymentRequest), "UIPaymentRequest", new { payReqId = paymentRequest.Id });

                AddMatch(results, parsedQuery.MatchQuery, new GlobalSearchResult
                {
                    Category = "Payment Request",
                    Title = string.IsNullOrWhiteSpace(paymentRequest.Title) ? paymentRequest.Id : paymentRequest.Title,
                    Subtitle = subtitle,
                    Url = url,
                    Keywords = $"{paymentRequest.Id} {paymentRequest.ReferenceId}"
                });
            }
        }

        private async Task AddTransactionResults(
            ICollection<GlobalSearchResult> results,
            string txSearch,
            int take,
            SearchContext context)
        {
            if (string.IsNullOrEmpty(txSearch))
                return;

            var startsWithPattern = CreateStartsWithILikePattern(txSearch);
            await using var dbContext = _dbContextFactory.CreateContext();

            var invoiceStoreIds = await GetStoreIdsWithPermission(context, Policies.CanViewInvoices);
            if (context.IsServerAdmin || invoiceStoreIds is { Length: > 0 })
            {
                IQueryable<PaymentData> paymentQuery = dbContext.Payments
                    .Include(data => data.InvoiceData)
                    .AsNoTracking();

                if (invoiceStoreIds is { Length: > 0 })
                    paymentQuery = paymentQuery.Where(data => invoiceStoreIds.Contains(data.InvoiceData.StoreDataId));

                paymentQuery = paymentQuery.Where(data =>
                    data.Id == txSearch || EF.Functions.ILike(data.Id, startsWithPattern, ILikeEscapeCharacter));

                var payments = await paymentQuery
                    .OrderByDescending(data => data.Created)
                    .Take(Math.Min(take, 12))
                    .ToArrayAsync();

                foreach (var payment in payments)
                {
                    var amount = payment.Amount is decimal amountValue
                        ? $"{amountValue.ToString(CultureInfo.InvariantCulture)} {payment.Currency}"
                        : payment.Currency;
                    var subtitle = $"{amount} · {payment.PaymentMethodId} · Invoice {payment.InvoiceDataId}";
                    var url = Url.Action(nameof(UIInvoiceController.Invoice), "UIInvoice",
                        new { storeId = payment.InvoiceData.StoreDataId, invoiceId = payment.InvoiceDataId });

                    AddMatch(results, string.Empty, new GlobalSearchResult
                    {
                        Category = "Transaction",
                        Title = payment.Id,
                        Subtitle = subtitle,
                        Url = url,
                        Keywords = $"{payment.InvoiceDataId} {payment.PaymentMethodId}"
                    });
                }
            }

            var walletStoreIds = await GetStoreIdsWithPermission(context, Policies.CanModifyStoreSettings);
            if (!context.IsServerAdmin && (walletStoreIds == null || walletStoreIds.Length == 0))
                return;

            var allowedStoreIds = walletStoreIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var walletTxObjects = await dbContext.WalletObjects
                .AsNoTracking()
                .Where(data => data.Type == WalletObjectData.Types.Tx &&
                               (data.Id == txSearch || EF.Functions.ILike(data.Id, startsWithPattern, ILikeEscapeCharacter)))
                .OrderBy(data => data.Id)
                .Take(Math.Min(take * 2, 30))
                .ToArrayAsync();

            foreach (var walletTx in walletTxObjects)
            {
                if (!WalletId.TryParse(walletTx.WalletId, out var walletId))
                    continue;

                if (!context.IsServerAdmin && (allowedStoreIds == null || !allowedStoreIds.Contains(walletId.StoreId)))
                    continue;

                var url = Url.Action(nameof(UIWalletsController.WalletTransactions), "UIWallets",
                    new { walletId = walletId.ToString() });

                AddMatch(results, string.Empty, new GlobalSearchResult
                {
                    Category = "Wallet Transaction",
                    Title = walletTx.Id,
                    Subtitle = $"{walletId.CryptoCode} wallet ({walletId.StoreId})",
                    Url = url,
                    Keywords = walletId.StoreId
                });
            }
        }

        private async Task<GlobalSearchResult[]> BuildPageResults(StoreData store, string query)
        {
            var results = new List<GlobalSearchResult>();

            var canModifyServer = await IsAuthorized(Policies.CanModifyServerSettings);
            var canViewProfile = await IsAuthorized(Policies.CanViewProfile);
            var canViewNotifications = await IsAuthorized(Policies.CanViewNotificationsForUser);

            if (store != null)
            {
                var canViewStoreSettings = await IsAuthorized(Policies.CanViewStoreSettings, store.Id);
                var canModifyStoreSettings = await IsAuthorized(Policies.CanModifyStoreSettings, store.Id);
                var canViewInvoices = await IsAuthorized(Policies.CanViewInvoices, store.Id);
                var canViewReports = await IsAuthorized(Policies.CanViewReports, store.Id);
                var canViewPaymentRequests = await IsAuthorized(Policies.CanViewPaymentRequests, store.Id);
                var canViewPullPayments = await IsAuthorized(Policies.CanViewPullPayments, store.Id);
                var canViewPayouts = await IsAuthorized(Policies.CanViewPayouts, store.Id);

                if (canModifyStoreSettings)
                {
                    AddPage(results, query, "Dashboard", Url.Action(nameof(UIStoresController.Dashboard), "UIStores", new { storeId = store.Id }), "Store");
                }
                if (canViewStoreSettings)
                {
                    AddPage(results, query, "Store Settings", Url.Action(nameof(UIStoresController.GeneralSettings), "UIStores", new { storeId = store.Id }), "Store", "general settings branding");
                    AddPage(results, query, "Rates", Url.Action(nameof(UIStoresController.Rates), "UIStores", new { storeId = store.Id }), "Store", "exchange");
                    AddPage(results, query, "Checkout Appearance", Url.Action(nameof(UIStoresController.CheckoutAppearance), "UIStores", new { storeId = store.Id }), "Store", "checkout");
                    AddPage(results, query, "Access Tokens", Url.Action(nameof(UIStoresController.ListTokens), "UIStores", new { storeId = store.Id }), "Store", "tokens api");
                    AddPage(results, query, "Store Users", Url.Action(nameof(UIStoresController.StoreUsers), "UIStores", new { storeId = store.Id }), "Store", "users");
                    AddPage(results, query, "Store Roles", Url.Action(nameof(UIStoresController.ListRoles), "UIStores", new { storeId = store.Id }), "Store", "roles");
                    AddPage(results, query, "Webhooks", Url.Action("Webhooks", "UIStoreWebhooks", new { area = "Webhooks", storeId = store.Id }), "Store", "webhooks");
                    AddPage(results, query, "Payout Processors", Url.Action("ConfigureStorePayoutProcessors", "UIPayoutProcessors", new { storeId = store.Id }), "Store", "payout processors");
                    AddPage(results, query, "Forms", Url.Action("FormsList", "UIForms", new { storeId = store.Id }), "Store", "forms");
                }
                if (canViewInvoices)
                {
                    AddPage(results, query, "Invoices", Url.Action(nameof(UIInvoiceController.ListInvoices), "UIInvoice", new { storeId = store.Id }), "Payments", "payments");
                    AddPage(results, query, "Create Invoice", Url.Action(nameof(UIInvoiceController.CreateInvoice), "UIInvoice", new { storeId = store.Id }), "Payments", "invoice");
                }
                if (canViewReports)
                {
                    AddPage(results, query, "Reporting", Url.Action(nameof(UIReportsController.StoreReports), "UIReports", new { storeId = store.Id }), "Payments", "reports");
                }
                if (canViewPaymentRequests)
                {
                    AddPage(results, query, "Payment Requests", Url.Action(nameof(UIPaymentRequestController.GetPaymentRequests), "UIPaymentRequest", new { storeId = store.Id }), "Payments", "payment requests");
                }
                if (canViewPullPayments)
                {
                    AddPage(results, query, "Pull Payments", Url.Action("PullPayments", "UIStorePullPayments", new { storeId = store.Id }), "Payments", "pull payments");
                }
                if (canViewPayouts)
                {
                    AddPage(results, query, "Payouts", Url.Action("Payouts", "UIStorePullPayments", new { storeId = store.Id }), "Payments", "payouts");
                }
            }

            if (canModifyServer)
            {
                AddPage(results, query, "Server Settings", Url.Action(nameof(UIServerController.Policies), "UIServer"), "Server", "server settings policies");
                AddPage(results, query, "Users", Url.Action(nameof(UIServerController.ListUsers), "UIServer"), "Server", "server settings users");
                AddPage(results, query, "Roles", Url.Action(nameof(UIServerController.ListRoles), "UIServer"), "Server", "server settings roles");
                AddPage(results, query, "Services", Url.Action(nameof(UIServerController.Services), "UIServer"), "Server", "server settings services");
                AddPage(results, query, "Branding", Url.Action(nameof(UIServerController.Branding), "UIServer"), "Server", "server settings branding");
                AddPage(results, query, "Translations", Url.Action(nameof(UIServerController.ListDictionaries), "UIServer"), "Server", "server settings translations");
                AddPage(results, query, "Maintenance", Url.Action(nameof(UIServerController.Maintenance), "UIServer"), "Server", "server settings maintenance");
                AddPage(results, query, "Logs", Url.Action(nameof(UIServerController.LogsView), "UIServer"), "Server", "server settings logs");
                AddPage(results, query, "Files", Url.Action(nameof(UIServerController.Files), "UIServer"), "Server", "server settings files storage");

                var pluginsUrl = Url.Action(nameof(UIServerController.ListPlugins), "UIServer");
                AddPage(results, query, "Manage Plugins", pluginsUrl, "Server", "plugins");
                if (!string.IsNullOrEmpty(pluginsUrl))
                {
                    AddPage(results, query, "Installed Plugins", $"{pluginsUrl}#plugins-installed", "Server", "plugins installed");
                    AddPage(results, query, "Plugin Directory", $"{pluginsUrl}#plugins-directory", "Server", "plugins directory");
                }
            }

            if (canViewProfile)
            {
                AddPage(results, query, "Manage Account", Url.Action(nameof(UIManageController.Index), "UIManage"), "Account", "profile account");
                AddPage(results, query, "Password", Url.Action(nameof(UIManageController.ChangePassword), "UIManage"), "Account", "password");
                AddPage(results, query, "Two-Factor Authentication", Url.Action(nameof(UIManageController.TwoFactorAuthentication), "UIManage"), "Account", "2fa security");
                AddPage(results, query, "API Keys", Url.Action(nameof(UIManageController.APIKeys), "UIManage"), "Account", "api keys");
                AddPage(results, query, "Notification Settings", Url.Action(nameof(UIManageController.NotificationSettings), "UIManage"), "Account", "notifications");
                AddPage(results, query, "Login Codes", Url.Action(nameof(UIManageController.LoginCodes), "UIManage"), "Account", "login codes");
            }

            if (canViewNotifications)
            {
                AddPage(results, query, "Notifications", Url.Action(nameof(UINotificationsController.Index), "UINotifications"), "Account", "notifications");
            }

            AddPage(results, query, "Home", Url.Action(nameof(UIHomeController.Index), "UIHome"), "General", "dashboard overview");
            AddPage(results, query, "Stores", Url.Action(nameof(UIUserStoresController.ListStores), "UIUserStores"), "General", "stores");

            return results.ToArray();
        }

        private async Task<string[]> GetStoreIdsWithPermission(SearchContext context, string policy)
        {
            if (context.StoreIdsByPolicy.TryGetValue(policy, out var cachedStoreIds))
                return cachedStoreIds;

            string[] storeIds;
            if (context.Store != null)
            {
                var allowed = await IsAuthorized(policy, context.Store.Id);
                storeIds = allowed ? [context.Store.Id] : Array.Empty<string>();
            }
            else if (context.IsServerAdmin)
            {
                storeIds = null;
            }
            else
            {
                if (string.IsNullOrEmpty(context.UserId))
                {
                    storeIds = Array.Empty<string>();
                }
                else
                {
                    context.UserStores ??= (await _storeRepository.GetStoresByUserId(context.UserId)).ToArray();
                    storeIds = context.UserStores
                        .Where(data => data.HasPermission(context.UserId, policy))
                        .Select(data => data.Id)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
            }

            context.StoreIdsByPolicy[policy] = storeIds;
            return storeIds;
        }

        private async Task<bool> IsAuthorized(string policy, string storeId = null)
        {
            var result = await _authorizationService.AuthorizeAsync(User, storeId, new PolicyRequirement(policy));
            return result.Succeeded;
        }

        private static void AddMatch(ICollection<GlobalSearchResult> results, string query, GlobalSearchResult result)
        {
            if (Matches(query, result.Title, result.Subtitle, result.Keywords, result.Category))
                results.Add(result);
        }

        private static void AddPage(ICollection<GlobalSearchResult> results, string query, string title, string url, string category, string keywords = null)
        {
            if (string.IsNullOrEmpty(url) || !Matches(query, title, category, keywords))
                return;

            results.Add(new GlobalSearchResult
            {
                Category = category,
                Title = title,
                Url = url,
                Keywords = keywords
            });
        }

        private static bool Matches(string query, params string[] values)
        {
            if (string.IsNullOrEmpty(query))
                return true;

            var terms = query.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0)
                return true;

            var searchableValues = values.Where(value => !string.IsNullOrEmpty(value)).ToArray();
            if (searchableValues.Length == 0)
                return false;

            return terms.All(term => searchableValues.Any(value =>
                value.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        private static string CreateContainsILikePattern(string value) => $"%{EscapeILikePattern(value)}%";
        private static string CreateStartsWithILikePattern(string value) => $"{EscapeILikePattern(value)}%";

        private static string EscapeILikePattern(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        }

        private static string GetTransactionSearch(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            var normalized = query.Trim();
            if (normalized.StartsWith("tx:", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[3..].Trim();
            else if (normalized.StartsWith("txid:", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[5..].Trim();

            if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[2..];

            if (normalized.Length is < 6 or > 128 || !normalized.All(Uri.IsHexDigit))
                return null;

            return normalized;
        }

        private static (DateTimeOffset Start, DateTimeOffset End)? GetDateRangeSearch(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            var value = query.Trim();
            if (value.StartsWith("date:", StringComparison.OrdinalIgnoreCase))
                value = value[5..].Trim();

            if (value.Equals("today", StringComparison.OrdinalIgnoreCase))
            {
                var start = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
                return (start, start.AddDays(1));
            }

            if (value.Equals("yesterday", StringComparison.OrdinalIgnoreCase))
            {
                var start = new DateTimeOffset(DateTimeOffset.UtcNow.Date.AddDays(-1), TimeSpan.Zero);
                return (start, start.AddDays(1));
            }

            if (TryParseDate(value, out var dayStart))
                return (dayStart, dayStart.AddDays(1));

            var rangeParts = value.Split("..", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (rangeParts.Length == 2 &&
                TryParseDate(rangeParts[0], out var rangeStart) &&
                TryParseDate(rangeParts[1], out var rangeEnd))
            {
                if (rangeEnd < rangeStart)
                    (rangeStart, rangeEnd) = (rangeEnd, rangeStart);

                return (rangeStart, rangeEnd.AddDays(1));
            }

            return null;
        }

        private static bool TryParseDate(string value, out DateTimeOffset dayStart)
        {
            dayStart = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (DateOnly.TryParseExact(value,
                    new[] { "yyyy-MM-dd", "yyyy/MM/dd", "MM/dd/yyyy", "M/d/yyyy" },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var dateOnly))
            {
                dayStart = new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                return true;
            }

            if (DateTimeOffset.TryParse(value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var dateTimeOffset))
            {
                dayStart = new DateTimeOffset(
                    dateTimeOffset.Year,
                    dateTimeOffset.Month,
                    dateTimeOffset.Day,
                    0,
                    0,
                    0,
                    TimeSpan.Zero);
                return true;
            }

            return false;
        }

        private sealed class SearchContext
        {
            public SearchContext(string userId, bool isServerAdmin, StoreData store)
            {
                UserId = userId;
                IsServerAdmin = isServerAdmin;
                Store = store;
            }

            public string UserId { get; }
            public bool IsServerAdmin { get; }
            public StoreData Store { get; }
            public StoreData[] UserStores { get; set; }
            public Dictionary<string, string[]> StoreIdsByPolicy { get; } = new(StringComparer.Ordinal);
        }

    }
}
