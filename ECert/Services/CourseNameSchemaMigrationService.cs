using System.Data;
using ECert.Data;
using Microsoft.EntityFrameworkCore;

namespace ECert.Services;

public class CourseNameSchemaMigrationService
{
    private readonly ECertDbContext _db;

    public CourseNameSchemaMigrationService(ECertDbContext db) => _db = db;

    public async Task EnsureAsync()
    {
        await EnsureColumnAsync("Courses", "CourseNameEnglish", "VARCHAR(200) NULL");
        await EnsureColumnAsync("Courses", "CourseNameArabic", "VARCHAR(200) NULL");
        await EnsureColumnAsync("Certificates", "CourseNameEnglish", "VARCHAR(200) NULL");
        await EnsureColumnAsync("Certificates", "CourseNameArabic", "VARCHAR(200) NULL");
        await EnsureColumnAsync("Certificates", "TraineeNameArabic", "VARCHAR(150) NULL");
        await EnsureColumnAsync("Certificates", "TraineeNameEnglish", "VARCHAR(150) NULL");

        await BackfillNullNamesAsync();
        await NormalizeCoursesAsync();
        await NormalizeCertificatesAsync();
    }

    private async Task BackfillNullNamesAsync()
    {
        await _db.Database.ExecuteSqlRawAsync(@"
UPDATE `Courses`
SET `CourseNameArabic` = COALESCE(NULLIF(`CourseNameArabic`, ''), `CourseName`),
    `CourseNameEnglish` = COALESCE(NULLIF(`CourseNameEnglish`, ''), `CourseName`)
WHERE `CourseNameArabic` IS NULL
   OR `CourseNameEnglish` IS NULL
   OR `CourseNameArabic` = ''
   OR `CourseNameEnglish` = '';");

        await _db.Database.ExecuteSqlRawAsync(@"
UPDATE `Certificates`
SET `CourseNameArabic` = COALESCE(NULLIF(`CourseNameArabic`, ''), `CourseName`),
    `CourseNameEnglish` = COALESCE(NULLIF(`CourseNameEnglish`, ''), `CourseName`)
WHERE `CourseNameArabic` IS NULL
   OR `CourseNameEnglish` IS NULL
   OR `CourseNameArabic` = ''
   OR `CourseNameEnglish` = '';");
    }

    private async Task NormalizeCoursesAsync()
    {
        var courses = await _db.Courses.ToListAsync();
        var changed = false;

        foreach (var course in courses)
        {
            var legacyName = course.CourseName?.Trim() ?? string.Empty;
            var englishName = course.CourseNameEnglish?.Trim() ?? string.Empty;
            var arabicName = course.CourseNameArabic?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(arabicName) && !string.IsNullOrWhiteSpace(legacyName))
            {
                course.CourseNameArabic = legacyName;
                arabicName = legacyName;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(englishName) && !string.IsNullOrWhiteSpace(legacyName))
            {
                course.CourseNameEnglish = legacyName;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(legacyName) && !string.IsNullOrWhiteSpace(arabicName))
            {
                course.CourseName = arabicName;
                changed = true;
            }
        }

        if (changed)
            await _db.SaveChangesAsync();
    }

    private async Task NormalizeCertificatesAsync()
    {
        var certificates = await _db.Certificates.ToListAsync();
        var changed = false;

        foreach (var certificate in certificates)
        {
            var legacyName = certificate.CourseName?.Trim() ?? string.Empty;
            var englishName = certificate.CourseNameEnglish?.Trim() ?? string.Empty;
            var arabicName = certificate.CourseNameArabic?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(arabicName) && !string.IsNullOrWhiteSpace(legacyName))
            {
                certificate.CourseNameArabic = legacyName;
                arabicName = legacyName;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(englishName) && !string.IsNullOrWhiteSpace(legacyName))
            {
                certificate.CourseNameEnglish = legacyName;
                changed = true;
            }
        }

        if (changed)
            await _db.SaveChangesAsync();
    }

    private async Task EnsureColumnAsync(string tableName, string columnName, string definitionSql)
    {
        if (await ColumnExistsAsync(tableName, columnName))
            return;

        await _db.Database.ExecuteSqlRawAsync($"ALTER TABLE `{tableName}` ADD COLUMN `{columnName}` {definitionSql};");
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
