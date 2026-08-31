using System.Data;
using ECert.Data;
using Microsoft.EntityFrameworkCore;

namespace ECert.Services;

public sealed class CourseInstructorSchemaMigrationService
{
    private readonly ECertDbContext _db;

    public CourseInstructorSchemaMigrationService(ECertDbContext db) => _db = db;

    public async Task EnsureAsync()
    {
        await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS `CourseInstructors` (
    `CourseInstructorId` INT NOT NULL AUTO_INCREMENT,
    `CourseId` INT NOT NULL,
    `InstructorId` INT NOT NULL,
    `SortOrder` INT NOT NULL DEFAULT 0,
    PRIMARY KEY (`CourseInstructorId`),
    UNIQUE KEY `UX_CourseInstructors_CourseId_InstructorId` (`CourseId`, `InstructorId`),
    KEY `IX_CourseInstructors_CourseId_SortOrder` (`CourseId`, `SortOrder`),
    KEY `IX_CourseInstructors_InstructorId` (`InstructorId`),
    CONSTRAINT `FK_CourseInstructors_Courses_CourseId`
        FOREIGN KEY (`CourseId`) REFERENCES `Courses` (`CourseId`) ON DELETE CASCADE,
    CONSTRAINT `FK_CourseInstructors_Instructors_InstructorId`
        FOREIGN KEY (`InstructorId`) REFERENCES `Instructors` (`InstructorId`) ON DELETE RESTRICT
) ENGINE=InnoDB CHARACTER SET utf8mb4;");

        await SeedPrimaryInstructorsAsync();
    }

    private async Task SeedPrimaryInstructorsAsync()
    {
        if (!await TableExistsAsync("Courses") || !await TableExistsAsync("Instructors"))
            return;

        await _db.Database.ExecuteSqlRawAsync(@"
INSERT INTO `CourseInstructors` (`CourseId`, `InstructorId`, `SortOrder`)
SELECT c.`CourseId`, c.`InstructorId`, 0
FROM `Courses` c
INNER JOIN `Instructors` i ON i.`InstructorId` = c.`InstructorId`
LEFT JOIN `CourseInstructors` ci
    ON ci.`CourseId` = c.`CourseId`
   AND ci.`InstructorId` = c.`InstructorId`
WHERE c.`InstructorId` IS NOT NULL
  AND c.`InstructorId` <> 0
  AND ci.`CourseInstructorId` IS NULL;");
    }

    private async Task<bool> TableExistsAsync(string tableName)
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
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = @tableName;";

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tableName";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);

            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }
        finally
        {
            if (shouldCloseConnection && connection.State == ConnectionState.Open)
                await connection.CloseAsync();
        }
    }
}
