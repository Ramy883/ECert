using ECert.Data;
using Microsoft.EntityFrameworkCore;

namespace ECert.Services;

/// <summary>ينشئ جدولَي الصندوق عبر سكربت CREATE IF NOT EXISTS متوافق مع MySQL.
/// لا يتم الاعتماد على EF EnsureCreated كلياً لأن EF قد لا يلتقط الجداول الجديدة فوراً.</summary>
public class CashboxSchemaMigrationService
{
    private readonly ECertDbContext _db;
    public CashboxSchemaMigrationService(ECertDbContext db) => _db = db;

    public async Task EnsureAsync()
    {
        await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS `CashboxTransfers` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `CourseId` INT NULL,
    `Amount` DECIMAL(18,2) NOT NULL,
    `Note` VARCHAR(500) NULL,
    `CreatedBy` VARCHAR(150) NOT NULL,
    `CreatedAt` DATETIME(6) NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_CashboxTransfers_CourseId` (`CourseId`),
    CONSTRAINT `FK_CashboxTransfers_Courses_CourseId`
        FOREIGN KEY (`CourseId`) REFERENCES `Courses` (`CourseId`)
        ON DELETE SET NULL
) CHARACTER SET utf8mb4;");

        await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS `CashboxWithdrawals` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `Amount` DECIMAL(18,2) NOT NULL,
    `Reason` VARCHAR(500) NOT NULL,
    `CreatedBy` VARCHAR(150) NOT NULL,
    `CreatedAt` DATETIME(6) NOT NULL,
    PRIMARY KEY (`Id`)
) CHARACTER SET utf8mb4;");
    }
}
