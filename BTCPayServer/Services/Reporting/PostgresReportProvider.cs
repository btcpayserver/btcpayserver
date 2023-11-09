#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Security;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Services.Reporting;

public class PostgresReportProvider : ReportProvider
{
    public string ReportName { get; set; }
    public override string Name => ReportName;
    public DynamicReportsSettings.DynamicReportSetting Setting { get; set; }

    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly IOptions<DatabaseOptions> _options;
    private readonly IHttpContextAccessor _httpContextAccessor;
    public PostgresReportProvider( ApplicationDbContextFactory dbContextFactory, 
        IOptions<DatabaseOptions> options, IHttpContextAccessor httpContextAccessor)
    {
        _dbContextFactory = dbContextFactory;
        _options = options;
        _httpContextAccessor = httpContextAccessor;
    }
    public override bool IsAvailable()
    {
        return _options.Value.DatabaseType == DatabaseType.Postgres &&
               Setting.AllowForNonAdmins || _httpContextAccessor.HttpContext?.User.IsInRole(Roles.ServerAdmin) is true;
    }
    public override async Task Query(QueryContext queryContext, CancellationToken cancellation)
    {
        await ExecuteQuery(_dbContextFactory, queryContext,Setting.Sql, cancellation);
    }

    public static async Task ExecuteQuery(ApplicationDbContextFactory dbContextFactory, QueryContext queryContext, string sql,
        CancellationToken cancellation)
    {
        await using var dbContext = dbContextFactory.CreateContext();
        await using var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        try
        {

        var rows = (await connection.QueryAsync(sql, new
        {
            queryContext.From,
            queryContext.To,
            queryContext.StoreId
        }))?.ToArray();
        if (rows?.Any() is true)
        {
            var firstRow = new RouteValueDictionary(rows.First());
            queryContext.ViewDefinition = new ViewDefinition()
            {
                Fields = firstRow.Keys.Select(f => new StoreReportResponse.Field(f, ObjectToFieldType(firstRow[f])))
                    .ToList(),
                Charts = new()
            };
        }
        else
        {
            return;
        }

        foreach (var row in rows)
        {
            var rowParsed = new RouteValueDictionary(row);
            var data = queryContext.CreateData();
            foreach (var field in queryContext.ViewDefinition.Fields)
            {
                var rowFieldValue = rowParsed[field.Name];
                field.Type ??= ObjectToFieldType(rowFieldValue);
                data.Add(rowFieldValue);
            }

            queryContext.Data.Add(data);
        }

        queryContext.ViewDefinition.Fields.Where(field => field.Type is null).ToList()
            .ForEach(field => field.Type = "string");
        
        }
        finally
        {
            
            await transaction.RollbackAsync(cancellation);
        }
    }

    private static string? ObjectToFieldType(object? value)
    {

        if (value is null)
            return null;
        if(value is string)
            return "string";
        if(value is DateTime)
            return "datetime";
        if(value is DateTimeOffset)
            return "datetime";
        if(value is bool)
            return "boolean";
        if(value is int)
            return "amount";
        if(value is decimal)
            return "amount";
        if(value is long)
            return "amount";
        if(value is double)
            return "amount";
            
            
        return "string";
    }
}
