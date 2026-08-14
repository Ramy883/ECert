using ECert.Data;
using Microsoft.EntityFrameworkCore;

namespace ECert.Services;

public class InvoiceSchemaMigrationService
{
    private readonly ECertDbContext _db;

    public InvoiceSchemaMigrationService(ECertDbContext db) => _db = db;

    public async Task EnsureAsync()
    {
        await EnsureColumnAsync("TraineeNameArabic", "VARCHAR(150) NULL");
        await EnsureColumnAsync("TraineeNameEnglish", "VARCHAR(150) NULL");
        await EnsureColumnAsync("CourseNameEnglish", "VARCHAR(200) NULL");
        await EnsureColumnAsync("CourseNameArabic", "VARCHAR(200) NULL");

        await _db.Database.ExecuteSqlRawAsync(@"
UPDATE `Invoices`
SET `TraineeNameArabic` = COALESCE(NULLIF(`TraineeNameArabic`, ''), `TraineeName`),
    `TraineeNameEnglish` = COALESCE(NULLIF(`TraineeNameEnglish`, ''), `TraineeName`),
    `CourseNameArabic` = COALESCE(NULLIF(`CourseNameArabic`, ''), `CourseName`),
    `CourseNameEnglish` = COALESCE(NULLIF(`CourseNameEnglish`, ''), `CourseName`)
WHERE `TraineeNameArabic` IS NULL
   OR `TraineeNameEnglish` IS NULL
   OR `CourseNameArabic` IS NULL
   OR `CourseNameEnglish` IS NULL
   OR `TraineeNameArabic` = ''
   OR `TraineeNameEnglish` = ''
   OR `CourseNameArabic` = ''
   OR `CourseNameEnglish` = '';");
    }

    private async Task EnsureColumnAsync(string columnName, string definitionSql)
    {
        if (await ColumnExistsAsync(columnName)) return;
        await _db.Database.ExecuteSqlRawAsync($"ALTER TABLE `Invoices` ADD COLUMN `{columnName}` {definitionSql};");
    }

    private async Task<bool> ColumnExistsAsync(string columnName)
    {
        var connection = _db.Database.GetDbConnection();
        var close = connection.State != System.Data.ConnectionState.Open;
        try
        {
            if (close) await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Invoices' AND COLUMN_NAME = @columnName;";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@columnName";
            parameter.Value = columnName;
            command.Parameters.Add(parameter);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }
        finally
        {
            if (close && connection.State == System.Data.ConnectionState.Open)
                await connection.CloseAsync();
        }
    }
}
