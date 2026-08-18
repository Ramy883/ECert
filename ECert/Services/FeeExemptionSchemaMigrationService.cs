using ECert.Data;
using Microsoft.EntityFrameworkCore;

namespace ECert.Services;

public sealed class FeeExemptionSchemaMigrationService
{
    private readonly ECertDbContext _db;

    public FeeExemptionSchemaMigrationService(ECertDbContext db) => _db = db;

    public async Task EnsureAsync()
    {
        await EnsureColumnAsync("Registrations", "ExemptionAmount", "DECIMAL(18,2) NOT NULL DEFAULT 0");
        await EnsureColumnAsync("Registrations", "ExemptionReason", "VARCHAR(500) NULL");
        await EnsureColumnAsync("Registrations", "ExemptionAppliedBy", "VARCHAR(255) NULL");
        await EnsureColumnAsync("Registrations", "ExemptionAppliedAt", "DATETIME NULL");
        await EnsureColumnAsync("Invoices", "OriginalAmount", "DECIMAL(18,2) NOT NULL DEFAULT 0");
        await EnsureColumnAsync("Invoices", "ExemptionAmount", "DECIMAL(18,2) NOT NULL DEFAULT 0");
        await EnsureColumnAsync("Invoices", "ExemptionReason", "VARCHAR(500) NULL");

        await _db.Database.ExecuteSqlRawAsync(@"
UPDATE `Invoices`
SET `OriginalAmount` = `TotalAmount`
WHERE `OriginalAmount` = 0 AND `TotalAmount` > 0;");
    }

    private async Task EnsureColumnAsync(string tableName, string columnName, string definitionSql)
    {
        if (await ColumnExistsAsync(tableName, columnName)) return;
        await _db.Database.ExecuteSqlRawAsync($"ALTER TABLE `{tableName}` ADD COLUMN `{columnName}` {definitionSql};");
    }

    private async Task<bool> ColumnExistsAsync(string tableName, string columnName)
    {
        var connection = _db.Database.GetDbConnection();
        var close = connection.State != System.Data.ConnectionState.Open;
        try
        {
            if (close) await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName AND COLUMN_NAME = @columnName;";
            var tableParameter = command.CreateParameter();
            tableParameter.ParameterName = "@tableName";
            tableParameter.Value = tableName;
            command.Parameters.Add(tableParameter);
            var columnParameter = command.CreateParameter();
            columnParameter.ParameterName = "@columnName";
            columnParameter.Value = columnName;
            command.Parameters.Add(columnParameter);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }
        finally
        {
            if (close && connection.State == System.Data.ConnectionState.Open)
                await connection.CloseAsync();
        }
    }
}
