using System.Data;
using ECert.Data;
using Microsoft.EntityFrameworkCore;

namespace ECert.Services;

public class AcademicSchemaMigrationService
{
    private readonly ECertDbContext _db;

    public AcademicSchemaMigrationService(ECertDbContext db) => _db = db;

    public async Task EnsureAsync()
    {
        await CreateAcademicTablesAsync();
        await SeedDefaultLevelsAsync();

        await EnsureColumnAsync("Courses", "RequiresAcademicDetails", "TINYINT(1) NOT NULL DEFAULT 0");

        await EnsureColumnAsync("Registrations", "UniversityId", "INT NULL");
        await EnsureColumnAsync("Registrations", "CollegeId", "INT NULL");
        await EnsureColumnAsync("Registrations", "AcademicSpecializationId", "INT NULL");
        await EnsureColumnAsync("Registrations", "AcademicLevel", "VARCHAR(80) NULL");
        await EnsureColumnAsync("Registrations", "UniversityNameSnapshot", "VARCHAR(160) NULL");
        await EnsureColumnAsync("Registrations", "CollegeNameSnapshot", "VARCHAR(160) NULL");
        await EnsureColumnAsync("Registrations", "SpecializationNameSnapshot", "VARCHAR(160) NULL");

        await EnsureIndexAsync("Registrations", "IX_Registrations_UniversityId", "UniversityId");
        await EnsureIndexAsync("Registrations", "IX_Registrations_CollegeId", "CollegeId");
        await EnsureIndexAsync("Registrations", "IX_Registrations_AcademicSpecializationId", "AcademicSpecializationId");
    }

    private async Task CreateAcademicTablesAsync()
    {
        await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS `Universities` (
    `UniversityId` INT NOT NULL AUTO_INCREMENT,
    `UniversityName` VARCHAR(160) NOT NULL,
    `IsActive` TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (`UniversityId`),
    UNIQUE KEY `UX_Universities_UniversityName` (`UniversityName`)
) ENGINE=InnoDB;");

        await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS `Colleges` (
    `CollegeId` INT NOT NULL AUTO_INCREMENT,
    `UniversityId` INT NOT NULL,
    `CollegeName` VARCHAR(160) NOT NULL,
    `IsActive` TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (`CollegeId`),
    UNIQUE KEY `UX_Colleges_UniversityId_CollegeName` (`UniversityId`, `CollegeName`),
    KEY `IX_Colleges_UniversityId` (`UniversityId`),
    CONSTRAINT `FK_Colleges_Universities_UniversityId` FOREIGN KEY (`UniversityId`) REFERENCES `Universities` (`UniversityId`) ON DELETE RESTRICT
) ENGINE=InnoDB;");

        await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS `AcademicSpecializations` (
    `AcademicSpecializationId` INT NOT NULL AUTO_INCREMENT,
    `CollegeId` INT NOT NULL,
    `SpecializationName` VARCHAR(160) NOT NULL,
    `IsActive` TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (`AcademicSpecializationId`),
    UNIQUE KEY `UX_AcademicSpecializations_CollegeId_SpecializationName` (`CollegeId`, `SpecializationName`),
    KEY `IX_AcademicSpecializations_CollegeId` (`CollegeId`),
    CONSTRAINT `FK_AcademicSpecializations_Colleges_CollegeId` FOREIGN KEY (`CollegeId`) REFERENCES `Colleges` (`CollegeId`) ON DELETE RESTRICT	) ENGINE=InnoDB;");

        await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS `AcademicLevelOptions` (
    `AcademicLevelOptionId` INT NOT NULL AUTO_INCREMENT,
    `AcademicSpecializationId` INT NOT NULL,
    `LevelName` VARCHAR(80) NOT NULL,
    `SortOrder` INT NOT NULL DEFAULT 0,
    `IsActive` TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (`AcademicLevelOptionId`),
    UNIQUE KEY `UX_AcademicLevelOptions_SpecializationId_LevelName` (`AcademicSpecializationId`, `LevelName`),
    KEY `IX_AcademicLevelOptions_AcademicSpecializationId` (`AcademicSpecializationId`),
    CONSTRAINT `FK_AcademicLevelOptions_AcademicSpecializations_AcademicSpecializationId` FOREIGN KEY (`AcademicSpecializationId`) REFERENCES `AcademicSpecializations` (`AcademicSpecializationId`) ON DELETE CASCADE
) ENGINE=InnoDB;");
    }

    private async Task SeedDefaultLevelsAsync()
    {
        var specializations = await _db.AcademicSpecializations
            .Select(s => s.AcademicSpecializationId)
            .ToListAsync();
        if (specializations.Count == 0) return;

        var levelCounts = await _db.AcademicLevelOptions
            .GroupBy(l => l.AcademicSpecializationId)
            .Select(g => new { AcademicSpecializationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AcademicSpecializationId, x => x.Count);

        var additions = new List<AcademicLevelOption>();
        foreach (var specializationId in specializations)
        {
            // Seed the suggested defaults only for a brand-new specialization.
            // Once an administrator customizes or removes levels, never recreate them on startup.
            if (levelCounts.ContainsKey(specializationId)) continue;

            for (var index = 0; index < AcademicLevelCatalog.Levels.Count; index++)
            {
                additions.Add(new AcademicLevelOption
                {
                    AcademicSpecializationId = specializationId,
                    LevelName = AcademicLevelCatalog.Levels[index],
                    SortOrder = index + 1
                });
            }
        }

        if (additions.Count > 0)
        {
            _db.AcademicLevelOptions.AddRange(additions);
            await _db.SaveChangesAsync();
        }
    }

    private async Task EnsureColumnAsync(string tableName, string columnName, string definitionSql)
    {
        if (!await TableExistsAsync(tableName) || await ColumnExistsAsync(tableName, columnName))
            return;

        await _db.Database.ExecuteSqlRawAsync($"ALTER TABLE `{tableName}` ADD COLUMN `{columnName}` {definitionSql};");
    }

    private async Task EnsureIndexAsync(string tableName, string indexName, string columnName)
    {
        if (!await TableExistsAsync(tableName) || await IndexExistsAsync(tableName, indexName))
            return;

        await _db.Database.ExecuteSqlRawAsync($"CREATE INDEX `{indexName}` ON `{tableName}` (`{columnName}`);");
    }

    private async Task<bool> TableExistsAsync(string tableName)
        => await InformationSchemaCountAsync(@"
SELECT COUNT(*)
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = @name;", tableName);

    private async Task<bool> ColumnExistsAsync(string tableName, string columnName)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        try
        {
            if (shouldCloseConnection) await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName AND COLUMN_NAME = @columnName;";
            var table = command.CreateParameter();
            table.ParameterName = "@tableName";
            table.Value = tableName;
            command.Parameters.Add(table);
            var column = command.CreateParameter();
            column.ParameterName = "@columnName";
            column.Value = columnName;
            command.Parameters.Add(column);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }
        finally
        {
            if (shouldCloseConnection && connection.State == ConnectionState.Open) await connection.CloseAsync();
        }
    }

    private async Task<bool> IndexExistsAsync(string tableName, string indexName)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        try
        {
            if (shouldCloseConnection) await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName AND INDEX_NAME = @indexName;";
            var table = command.CreateParameter();
            table.ParameterName = "@tableName";
            table.Value = tableName;
            command.Parameters.Add(table);
            var index = command.CreateParameter();
            index.ParameterName = "@indexName";
            index.Value = indexName;
            command.Parameters.Add(index);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }
        finally
        {
            if (shouldCloseConnection && connection.State == ConnectionState.Open) await connection.CloseAsync();
        }
    }

    private async Task<bool> InformationSchemaCountAsync(string sql, string name)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        try
        {
            if (shouldCloseConnection) await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@name";
            parameter.Value = name;
            command.Parameters.Add(parameter);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }
        finally
        {
            if (shouldCloseConnection && connection.State == ConnectionState.Open) await connection.CloseAsync();
        }
    }
}
