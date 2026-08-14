using System.Data;
using ECert.Data;
using Microsoft.EntityFrameworkCore;

namespace ECert.Services;

public class RegistrationNameSchemaMigrationService
{
    private readonly ECertDbContext _db;

    public RegistrationNameSchemaMigrationService(ECertDbContext db) => _db = db;

    public async Task EnsureAsync()
    {
        await EnsureColumnAsync("Registrations", "FullNameArabic", "VARCHAR(100) NULL");
        await EnsureColumnAsync("Registrations", "FullNameEnglish", "VARCHAR(100) NULL");
        await EnsureColumnAsync("Registrations", "Gender", "VARCHAR(20) NULL");
        await DropColumnIfExistsAsync("Registrations", "DocumentPath");
        await DropColumnIfExistsAsync("Registrations", "DocumentOriginalName");

        await _db.Database.ExecuteSqlRawAsync(@"
UPDATE `Registrations`
SET `FullNameArabic` = COALESCE(NULLIF(`FullNameArabic`, ''), `FullName`),
    `FullNameEnglish` = COALESCE(NULLIF(`FullNameEnglish`, ''), `FullName`)
WHERE `FullNameArabic` IS NULL
   OR `FullNameEnglish` IS NULL
   OR `FullNameArabic` = ''
   OR `FullNameEnglish` = '';");
    }

    private async Task EnsureColumnAsync(string tableName, string columnName, string definitionSql)
    {
        if (await ColumnExistsAsync(tableName, columnName))
            return;

        await _db.Database.ExecuteSqlRawAsync($"ALTER TABLE `{tableName}` ADD COLUMN `{columnName}` {definitionSql};");
    }

    private async Task DropColumnIfExistsAsync(string tableName, string columnName)
    {
        if (!await ColumnExistsAsync(tableName, columnName))
            return;

        await _db.Database.ExecuteSqlRawAsync($"ALTER TABLE `{tableName}` DROP COLUMN `{columnName}`;");
    }

    private async Task<bool> ColumnExistsAsync(string tableName, string columnName)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        try
        {
            if (shouldCloseConnection)
                await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(*)
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = @tableName
  AND COLUMN_NAME = @columnName;";

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
            if (shouldCloseConnection && connection.State == ConnectionState.Open)
                await connection.CloseAsync();
        }
    }
}
