using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using Microsoft.Extensions.Logging;
using VSS;
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
    private readonly ILogger<VSSController> _logger;

    public VSSController(ApplicationDbContextFactory dbContextFactory,
        UserManager<ApplicationUser> userManager, BTCPayAppState appState, ILogger<VSSController> logger)
    {
        _dbContextFactory = dbContextFactory;
        _userManager = userManager;
        _appState = appState;
        _logger = logger;
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

    private async Task<bool> VerifyMaster(long deviceIdentifier)
    {
        var userId = _userManager.GetUserId(User);
        return await _appState.IsMaster(userId, deviceIdentifier);
    }

    [HttpPost(HttpVSSAPIClient.PUT_OBJECTS)]
    [MediaTypeConstraint("application/octet-stream")]
    public async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken)
    {
        if (!await VerifyMaster(request.GlobalVersion))
            return SetResult<PutObjectResponse>(BadRequest(new ErrorResponse()
            {
                ErrorCode = ErrorCode.ConflictException, Message = "Global version mismatch"
            }));

        var userId = _userManager.GetUserId(User);

        await using var dbContext = _dbContextFactory.CreateContext();
        return await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async async =>
        {
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
                _logger.LogInformation(
                    $"VSS backup request processed: {string.Join(", ", request.TransactionItems.Select(data => data.Key))}");
                await _appState.GracefulDisconnect(userId);
                return new PutObjectResponse();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while processing vss backup request");
                await dbContextTransaction.RollbackAsync(cancellationToken);
                return SetResult<PutObjectResponse>(BadRequest(new ErrorResponse()
                {
                    ErrorCode = ErrorCode.ConflictException, Message = e.Message
                }));
            }
        }, cancellationToken);
    }

    [HttpPost(HttpVSSAPIClient.DELETE_OBJECT)]
    [MediaTypeConstraint("application/octet-stream")]
    public async Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        await using var dbContext = _dbContextFactory.CreateContext();
        var store = await dbContext.AppStorageItems
            .Where(data => data.Key == request.KeyValue.Key && data.UserId == userId &&
                           data.Version == request.KeyValue.Version)
            .ExecuteDeleteAsync(cancellationToken: cancellationToken);
        return store == 0
            ? SetResult<DeleteObjectResponse>(
                new NotFoundObjectResult(new ErrorResponse()
                {
                    ErrorCode = ErrorCode.NoSuchKeyException, Message = "Key not found"
                }))
            : new DeleteObjectResponse();
    }

    [HttpPost(HttpVSSAPIClient.LIST_KEY_VERSIONS)]
    public async Task<ListKeyVersionsResponse> ListKeyVersionsAsync(ListKeyVersionsRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        await using var dbContext = _dbContextFactory.CreateContext();
        var items = await dbContext.AppStorageItems
            .Where(data => data.UserId == userId && data.Key != "masterDevice")
            .Select(data => new KeyValue() {Key = data.Key, Version = data.Version})
            .ToListAsync(cancellationToken: cancellationToken);
        return new ListKeyVersionsResponse {KeyVersions = {items}};
    }
}
