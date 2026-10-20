using Astra.Api.Infrastructure;
using Astra.Api.Modules;
using Astra.Api.Security;
using Microsoft.Data.SqlClient;

namespace Astra.Tests;

public class NumberingTests
{
    private static SqlConnection Connection() => new(new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING")) { InitialCatalog = "AstraMigration" }.ConnectionString);

    [Theory]
    [InlineData("aahc01", "AAHC01")]
    [InlineData(" ACF-S01 ", "")]
    [InlineData("1AAHC", "")]
    [InlineData("A", "")]
    [InlineData("AAHC1234567890123", "")]
    public void PrefixValidationMatchesSiteNumberingRules(string input, string expected)
    {
        if (expected.Length > 0) Assert.Equal(expected, TicketNumbering.NormalizePrefix(input));
        else Assert.Equal(400, Assert.Throws<ApiFault>(() => TicketNumbering.NormalizePrefix(input)).Status);
    }

    [SqlServerFact]
    public async Task SiteNumbersAreIndependentTransactionalAndNeverTruncated()
    {
        await using var connection = Connection();
        await connection.OpenAsync();
        var sites = await Database.QueryAsync(connection, "SELECT TOP (2) id,client_id,ticket_prefix FROM dbo.sites WHERE active=1 ORDER BY id");
        Assert.Equal(2, sites.Count);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        foreach (var site in sites)
        {
            var parameters = new Dictionary<string, object?> { ["id"] = site["id"] };
            await Database.ExecuteAsync(connection, "DELETE dbo.site_ticket_sequences WHERE site_id=@id; INSERT dbo.site_ticket_sequences(site_id,next_number) VALUES(@id,1)", parameters, transaction);
        }
        var first = (Guid)sites[0]["id"]!; var firstClient = (Guid)sites[0]["client_id"]!;
        Assert.Equal((string)sites[0]["ticket_prefix"]! + "-000001", await TicketNumbering.NextTicketAsync(connection, transaction, firstClient, first));
        Assert.Equal((string)sites[0]["ticket_prefix"]! + "-000002", await TicketNumbering.NextTicketAsync(connection, transaction, firstClient, first));
        Assert.Equal((string)sites[1]["ticket_prefix"]! + "-000001", await TicketNumbering.NextTicketAsync(connection, transaction, (Guid)sites[1]["client_id"]!, (Guid)sites[1]["id"]!));
        await Database.ExecuteAsync(connection, "UPDATE dbo.site_ticket_sequences SET next_number=1000000 WHERE site_id=@id", new Dictionary<string, object?> { ["id"] = first }, transaction);
        Assert.EndsWith("-1000000", await TicketNumbering.NextTicketAsync(connection, transaction, firstClient, first));
        var fault = await Assert.ThrowsAsync<ApiFault>(() => TicketNumbering.NextTicketAsync(connection, transaction, Guid.NewGuid(), first));
        Assert.Equal(400, fault.Status);
        await transaction.RollbackAsync();
    }

    [SqlServerFact]
    public async Task PrefixReservationCannotStealHistoricOrOtherSitePrefixes()
    {
        await using var connection = Connection(); await connection.OpenAsync();
        var site = (await Database.QueryAsync(connection, "SELECT TOP (1) id,client_id,ticket_prefix FROM dbo.sites ORDER BY id")).Single();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        Assert.Equal(site["ticket_prefix"], await TicketNumbering.ReserveAsync(connection, transaction, "SITE", (Guid)site["id"]!, (string)site["ticket_prefix"]!));
        var conflict = await Assert.ThrowsAsync<ApiFault>(() => TicketNumbering.ReserveAsync(connection, transaction, "SITE", Guid.NewGuid(), (string)site["ticket_prefix"]!));
        Assert.Equal(409, conflict.Status);
        var prefix = await TicketNumbering.ReserveAsync(connection, transaction, "SITE", Guid.NewGuid(), null, (Guid)site["client_id"]!);
        Assert.True(prefix.Length is >= 2 and <= 16);
        Assert.NotEqual(site["ticket_prefix"], prefix);
        await transaction.RollbackAsync();
    }

    [SqlServerFact]
    public async Task ConcurrentReservationsGetUniqueNumbers()
    {
        await using var setup = Connection(); await setup.OpenAsync();
        var site = (await Database.QueryAsync(setup, "SELECT TOP (1) id,client_id,ticket_prefix FROM dbo.sites ORDER BY id DESC")).Single();
        var parameters = new Dictionary<string, object?> { ["id"] = site["id"] };
        var previous = await Database.QueryAsync(setup, "SELECT next_number FROM dbo.site_ticket_sequences WHERE site_id=@id", parameters);
        // Use a temporary site counter in the isolated database and restore its
        // exact prior state after testing the separately committed transactions.
        await Database.ExecuteAsync(setup, "IF NOT EXISTS(SELECT 1 FROM dbo.site_ticket_sequences WHERE site_id=@id) INSERT dbo.site_ticket_sequences(site_id,next_number) VALUES(@id,1)", parameters);
        try
        {
            var numbers = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            {
                await using var connection = Connection(); await connection.OpenAsync();
                await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
                var number = await TicketNumbering.NextTicketAsync(connection, transaction, (Guid)site["client_id"]!, (Guid)site["id"]!);
                await transaction.CommitAsync(); return number;
            }));
            Assert.Equal(8, numbers.Distinct().Count());
        }
        finally
        {
            if (previous.Count == 0) await Database.ExecuteAsync(setup, "DELETE dbo.site_ticket_sequences WHERE site_id=@id", parameters);
            else { parameters["next"] = previous[0]["next_number"]; await Database.ExecuteAsync(setup, "UPDATE dbo.site_ticket_sequences SET next_number=@next WHERE site_id=@id", parameters); }
        }
    }
}
