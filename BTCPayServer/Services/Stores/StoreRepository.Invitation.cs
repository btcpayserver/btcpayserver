#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Events;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Services.Stores;

public partial class StoreRepository
{
    public record CreateStoreInvitationResult
    {
        public record Success(GeneratedInvitation Invitation) : CreateStoreInvitationResult;

        public record InvalidRole : CreateStoreInvitationResult
        {
            public override string ToString() => "The roleId doesn't exist";
        }

        public record AlreadyMember : CreateStoreInvitationResult
        {
            public override string ToString() => "The user already has access to this store.";
        }
    }

    public async Task<CreateStoreInvitationResult> CreateStoreInvitation(string storeId, string userId, StoreRoleId? roleId, string? invitedByUserId)
    {
        ArgumentNullException.ThrowIfNull(storeId);
        AssertStoreRoleIfNeeded(storeId, roleId);
        roleId ??= await GetDefaultRole();
        if (await GetStoreRole(roleId) is null)
            return new CreateStoreInvitationResult.InvalidRole();

        await using var ctx = _ContextFactory.CreateContext();
        if (await ctx.UserStore.AnyAsync(u => u.StoreDataId == storeId && u.ApplicationUserId == userId))
            return new CreateStoreInvitationResult.AlreadyMember();

        var created = DateTimeOffset.UtcNow;
        var token = GenerateInvitationToken();
        var expiresAt = created + StoreInvitationRow.Lifetime;
        await ctx.Database.GetDbConnection().ExecuteAsync(
            """
            INSERT INTO store_invitations (store_id, user_id, role_id, invited_by_user_id, created, expires_at, token_hash)
            VALUES (@storeId, @userId, @roleId, @invitedByUserId, @created, @expiresAt, @tokenHash)
            ON CONFLICT (store_id, user_id)
            DO UPDATE SET
                invited_by_user_id = @invitedByUserId,
                created = @created,
                expires_at = @expiresAt,
                token_hash = @tokenHash,
                role_id = @roleId
            """,
            new
            {
                storeId,
                userId,
                roleId = roleId.Id,
                invitedByUserId,
                created,
                expiresAt,
                tokenHash = StoreInvitationRow.HashToken(token)
            });

        var invitation = new StoreInvitationRow(storeId, userId, roleId.Id, invitedByUserId, created.UtcDateTime, expiresAt.UtcDateTime);
        return new CreateStoreInvitationResult.Success(new(invitation, token));
    }

    public async Task<StoreInvitationExtendedRow[]> GetStoreInvitations(string storeId)
    {
        await using var ctx = _ContextFactory.CreateContext();
        return (await ctx.Database.GetDbConnection()
            .QueryAsync<StoreInvitationExtendedRow>($"""
                                             SELECT {StoreInvitationExtendedRowColumns}
                                             FROM store_invitations
                                             JOIN "AspNetUsers" u ON u."Id" = user_id
                                             JOIN "Stores" s ON s."Id" = store_id
                                             WHERE store_id = @storeId
                                             ORDER BY created
                                             """, new { storeId})).ToArray();
    }

    private const string StoreInvitationRowColumns = """
                                                        store_id AS "StoreId",
                                                        user_id AS "UserId",
                                                        role_id AS "RoleId",
                                                        invited_by_user_id AS "InvitedByUserId",
                                                        created AS "Created",
                                                        expires_at AS "ExpiresAt"
                                                        """;
    private const string StoreInvitationExtendedRowColumns = """
                                                     store_id AS "StoreId",
                                                     user_id AS "UserId",
                                                     role_id AS "RoleId",
                                                     invited_by_user_id AS "InvitedByUserId",
                                                     created AS "Created",
                                                     expires_at AS "ExpiresAt",
                                                     u."Email" AS "UserEmail",
                                                     s."StoreName"
                                                     """;

    public async Task<GeneratedInvitation?> ResendStoreInvitation(string storeId, string userId)
    {
        await using var ctx = _ContextFactory.CreateContext();
        var token = GenerateInvitationToken();
        var created = DateTimeOffset.UtcNow;
        var invitation = await ctx.Database.GetDbConnection().QuerySingleOrDefaultAsync<StoreInvitationRow>(
            $"""
            UPDATE store_invitations
            SET created = @created,
                expires_at = @expiresAt,
                token_hash = @tokenHash
            WHERE store_id = @storeId AND user_id = @userId
            RETURNING {StoreInvitationRowColumns}
            """,
            new
            {
                storeId,
                userId,
                created,
                expiresAt = created + StoreInvitationRow.Lifetime,
                tokenHash = StoreInvitationRow.HashToken(token)
            });
        return invitation is null
            ? null
            : new(invitation, token);
    }

    public async Task<AddOrUpdateStoreUserResult?> AcceptStoreInvitation(string userId, string token)
    {
        ArgumentNullException.ThrowIfNull(userId);

        var tokenHash = StoreInvitationRow.HashToken(token);
        AddOrUpdateStoreUserResult? result = null;
        StoreInvitationRow? acceptedInvitation = null;
        await using var strategyCtx = _ContextFactory.CreateContext();
        await strategyCtx.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            result = null;
            acceptedInvitation = null;
            await using var ctx = _ContextFactory.CreateContext();
            await using var tx = await ctx.Database.BeginTransactionAsync();
            var invitation = await ctx.Database.GetDbConnection()
                .QueryFirstOrDefaultAsync<StoreInvitationRow>($"""
                                                               DELETE FROM store_invitations WHERE user_id = @userId AND token_hash = @tokenHash
                                                               RETURNING {StoreInvitationRowColumns}
                                                               """,
                    new { userId, tokenHash }, tx.GetDbTransaction());
            if (invitation is null)
                return;
            if (invitation.IsExpired())
            {
                await tx.CommitAsync();
                result = new AddOrUpdateStoreUserResult.Expired();
                return;
            }
            if (!await ctx.StoreRoles.AnyAsync(r => r.Id == invitation.RoleId))
            {
                result = new AddOrUpdateStoreUserResult.InvalidRole();
                return;
            }

            ctx.UserStore.Add(new UserStore { StoreDataId = invitation.StoreId, ApplicationUserId = userId, StoreRoleId = invitation.RoleId });
            try
            {
                await ctx.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateException)
            {
                result = new AddOrUpdateStoreUserResult.DuplicateRole(StoreRoleId.Parse(invitation.RoleId));
                return;
            }
            result = new AddOrUpdateStoreUserResult.Success();
            acceptedInvitation = invitation;
        });

        if (acceptedInvitation is not null)
        {
            await using var ctx = _ContextFactory.CreateContext();
            await ctx.Users.UpdateStoreNoActiveUserForStores([acceptedInvitation.StoreId]);
            _eventAggregator.Publish(new StoreUserEvent.Added(acceptedInvitation.StoreId, userId, acceptedInvitation.RoleId));
            _eventAggregator.Publish(new StoreUserInvitationEvent.Accepted(acceptedInvitation));
        }
        return result;
    }

    private static string GenerateInvitationToken()
        => Encoders.Base58.EncodeData(RandomUtils.GetBytes(24));

    /// <summary>Announces an invitation once the caller has built its link.</summary>
    public Task NotifyStoreInvitation(StoreInvitationRow invitation, string invitationLink)
    {
        _eventAggregator.Publish(new StoreUserInvitationEvent.Created(invitation, invitationLink));
        return Task.CompletedTask;
    }

    public async Task<StoreInvitationExtendedRow?> GetStoreInvitationByToken(string token, string? storeId = null, string? userId = null)
    {
        if (string.IsNullOrEmpty(token))
            return null;

        List<string> conditions = new();
        DynamicParameters parameters = new();
        conditions.Add("token_hash = @tokenHash");
        parameters.Add("tokenHash", StoreInvitationRow.HashToken(token));
        if (storeId is not null)
        {
            conditions.Add("store_id = @storeId");
            parameters.Add("storeId", storeId);
        }
        if (userId is not null)
        {
            conditions.Add("user_id = @userId");
            parameters.Add("userId", userId);
        }

        await using var ctx = _ContextFactory.CreateContext();
        return await ctx.Database.GetDbConnection()
            .QueryFirstOrDefaultAsync<StoreInvitationExtendedRow>($"""
                                                    SELECT {StoreInvitationExtendedRowColumns}
                                                    FROM store_invitations
                                                    JOIN "AspNetUsers" u ON u."Id" = user_id
                                                    JOIN "Stores" s ON s."Id" = store_id
                                                     WHERE {string.Join(" AND ", conditions)}
                                                    """, parameters);
    }

    public record GeneratedInvitation(StoreInvitationRow Invitation, string Token);

    public async Task<bool> DeleteStoreInvitation(string storeId, string userId)
    {
        await using var ctx = _ContextFactory.CreateContext();
        return await ctx.Database.GetDbConnection().ExecuteAsync(
            """
            DELETE FROM store_invitations
            WHERE store_id = @storeId AND user_id = @userId
            """,
            new { storeId, userId }) > 0;
    }

    public async Task<bool> DeleteStoreInvitationByToken(string userId, string token)
    {
        await using var ctx = _ContextFactory.CreateContext();
        return await ctx.Database.GetDbConnection().ExecuteAsync(
            """
            DELETE FROM store_invitations
            WHERE user_id = @userId AND token_hash = @tokenHash
            """,
            new { userId, tokenHash = StoreInvitationRow.HashToken(token) }) > 0;
    }
}
