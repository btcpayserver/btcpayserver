# Implementation Plan: TX ID Search for BTCPay Server Wallet

## Issue Reference
- Issue: #7091
- URL: https://github.com/btcpayserver/btcpayserver/issues/7091
- Title: "Add support for search, specifically for TX id inside the wallet view"

## Current State
The wallet transactions view (`WalletTransactions.cshtml`) displays transactions with:
- Date filtering (already implemented)
- Label filtering (already implemented)
- Export functionality
- Mass actions

## Proposed Solution

### 1. Backend Changes (UIWalletsController.cs)

**Location**: Line 606 in `BTCPayServer/Controllers/UIWalletsController.cs`

**Add new parameter**:
```csharp
string? searchQuery = null  // Add to WalletTransactions method signature
```

**Implementation**:
- Add `searchQuery` parameter to the method signature
- Filter transactions by `TransactionId` containing the search query
- Apply filter after fetching transactions (similar to `labelFilter`)
- Pass searchQuery to pagination query for consistency

**Code Location**: Around line 690-700, inside the transaction processing loop:
```csharp
if (searchQuery != null)
{
    model.PaginationQuery = model.PaginationQuery ?? new Dictionary<string, object>();
    model.PaginationQuery["searchQuery"] = searchQuery;
}

// Inside the filtering logic (after labelFilter check)
if (searchQuery != null &&
    !vm.Id.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
    continue;
```

### 2. Frontend Changes (WalletTransactions.cshtml)

**Location**: `BTCPayServer/Views/UIWallets/WalletTransactions.cshtml`

**Add search input** in the `#Dropdowns` div (after label filter dropdown, before Export):
```html
<div class="d-inline-flex" id="Search">
    <form method="get" class="d-inline-flex">
        <input type="hidden" name="walletId" value="@walletId" />
        @if (!string.IsNullOrEmpty(labelFilter))
        {
            <input type="hidden" name="labelFilter" value="@labelFilter" />
        }
        <div class="input-group">
            <input type="text"
                   name="searchQuery"
                   class="form-control"
                   placeholder="@StringLocalizer["Search by transaction ID"]"
                   value="@Context.Request.Query["searchQuery"]"
                   autocomplete="off" />
            @if (!string.IsNullOrEmpty(Context.Request.Query["searchQuery"]))
            {
                <a asp-action="WalletTransactions"
                   asp-route-walletId="@walletId"
                   asp-route-labelFilter="@labelFilter"
                   class="btn btn-secondary input-group-text">
                    <vc:icon symbol="actions-clear" />
                </a>
            }
        </div>
    </form>
</div>
```

### 3. Testing Strategy

**Manual Testing**:
1. Navigate to wallet transactions page
2. Enter partial transaction ID (e.g., first 8 characters)
3. Verify results are filtered correctly
4. Verify clear button appears when searching
5. Verify label filter still works with search
6. Verify pagination maintains search state

**Edge Cases**:
- Empty search query (should show all)
- Non-matching search (should show empty list)
- Case-insensitive search
- Search with label filter combined
- Pagination with active search

### 4. Files to Modify

1. `BTCPayServer/Controllers/UIWalletsController.cs` - Add search parameter and filtering logic
2. `BTCPayServer/Views/UIWallets/WalletTransactions.cshtml` - Add search input UI

### 5. Estimated Effort

- **Backend Changes**: 30 minutes
- **Frontend Changes**: 30 minutes
- **Testing**: 30 minutes
- **Total**: ~1.5-2 hours

### 6. ROI Analysis

- **Complexity**: Low (simple filter on existing data)
- **Token Cost**: ~$8-12
- **Value**: Medium-High (requested feature, good visibility)
- **Profit Margin**: High (quick implementation, portfolio value)

---

Ready to implement! 🚀
