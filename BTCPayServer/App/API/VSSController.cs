using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayApp.VSS;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.App.BackupStorage;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using Google.Protobuf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VSSProto;

namespace BTCPayServer.App.API;

[ApiController]
[ResultOverrideFilter]
[ProtobufFormatter]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.GreenfieldBearer)]
[Route("vss")]
public class VSSController : Controller, IVSSAPI
{
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly BTCPayAppState _appState;

    public VSSController(ApplicationDbContextFactory dbContextFactory,
        UserManager<ApplicationUser> userManager, BTCPayAppState appState)
    {
        _dbContextFactory = dbContextFactory;
        _userManager = userManager;
        _appState = appState;
    }

    [HttpPost(HttpVSSAPIClient.GET_OBJECT)]
    [MediaTypeConstraint("application/octet-stream")]
    public async Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken)
    {
        
        var userId = _userManager.GetUserId(User);
        await using var dbContext = _dbContextFactory.CreateContext();
        var store = await dbContext.AppStorageItems.SingleOrDefaultAsync(data =>
            data.Key == request.Key && data.UserId == userId, cancellationToken: cancellationToken);
        if (store == null)
        {
            return SetResult<GetObjectResponse>(
                new NotFoundObjectResult(new ErrorResponse()
                {
                    ErrorCode = ErrorCode.NoSuchKeyException, Message = "Key not found"
                }));
        }

        return new GetObjectResponse()
        {
            Value = new KeyValue()
            {
                Key = store.Key, Value = ByteString.CopyFrom(store.Value), Version = store.Version
            }
        };
    }


    private T SetResult<T>(IActionResult result)
    {
        HttpContext.Items["Result"] = result;
        return default;
    }

    private bool VerifyGlobalVersion(long globalVersion)
    {
        
        var userId = _userManager.GetUserId(User);
        if (!_appState.GroupToConnectionId.TryGetValues(userId, out var connections))
        {
            return false;
        }
        var node = _appState.NodeToConnectionId.SingleOrDefault(data => connections.Contains(data.Value));
        if (node.Key == null || node.Value == null)
        {
            return false;
        }
        // This has a high collision rate, but we're not expecting something insane here since we have auth and other checks in place. 
        return globalVersion ==( node.Key + node.Value).GetHashCode();
    }

    [HttpPost(HttpVSSAPIClient.PUT_OBJECTS)]
    [MediaTypeConstraint("application/octet-stream")]
    public async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken)
    {

        if (!VerifyGlobalVersion(request.GlobalVersion))
            return SetResult<PutObjectResponse>(BadRequest(new ErrorResponse()
            {
                ErrorCode = ErrorCode.ConflictException, Message = "Global version mismatch"
            }));
        
        var userId = _userManager.GetUserId(User);
        
        await using var dbContext = _dbContextFactory.CreateContext();

        await using var dbContextTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (request.TransactionItems.Any())
            {
                var items = request.TransactionItems.Select(data => new AppStorageItemData()
                {
                    Key = data.Key, Value = data.Value.ToByteArray(), UserId = userId, Version = data.Version
                });
                await dbContext.AppStorageItems.AddRangeAsync(items, cancellationToken);
            }

            if (request.DeleteItems.Any())
            {
                var deleteQuery = request.DeleteItems.Aggregate(
                    dbContext.AppStorageItems.Where(data => data.UserId == userId),
                    (current, key) => current.Where(data => data.Key == key.Key && data.Version == key.Version));
                await deleteQuery.ExecuteDeleteAsync(cancellationToken: cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await dbContextTransaction.CommitAsync(cancellationToken);
        }
        catch (Exception e)
        {
            await dbContextTransaction.RollbackAsync(cancellationToken);
            return SetResult<PutObjectResponse>(BadRequest(new ErrorResponse()
            {
                ErrorCode = ErrorCode.ConflictException, Message = e.Message
            }));
        }


        return new PutObjectResponse();
    }

    [HttpPost(HttpVSSAPIClient.DELETE_OBJECT)]
    [MediaTypeConstraint("application/octet-stream")]
    public async Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request, CancellationToken cancellationToken)
    {
        

        var userId = _userManager.GetUserId(User);
        await using var dbContext = _dbContextFactory.CreateContext();
        var store = await dbContext.AppStorageItems
            .Where(data => data.Key == request.KeyValue.Key && data.UserId == userId &&
                           data.Version == request.KeyValue.Version).ExecuteDeleteAsync(cancellationToken: cancellationToken);
        return store == 0
            ? SetResult<DeleteObjectResponse>(
                new NotFoundObjectResult(new ErrorResponse()
                {
                    ErrorCode = ErrorCode.NoSuchKeyException, Message = "Key not found"
                }))
            : new DeleteObjectResponse();
    }

    [HttpPost(HttpVSSAPIClient.LIST_KEY_VERSIONS)]
    public async Task<ListKeyVersionsResponse> ListKeyVersionsAsync(ListKeyVersionsRequest request, CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        await using var dbContext = _dbContextFactory.CreateContext();
        var items = await dbContext.AppStorageItems
            .Where(data => data.UserId == userId)
            .Select(data => new KeyValue() {Key = data.Key, Version = data.Version}).ToListAsync(cancellationToken: cancellationToken);
        return new ListKeyVersionsResponse {KeyVersions = {items}};
    }
}
