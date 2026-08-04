using System.Data;
using ECert.Data;
using ECert.Models;
using Microsoft.EntityFrameworkCore;

namespace ECert.Services;

public class CertificateSchemaMigrationService
{
    private readonly ECertDbContext _db;
    private readonly CertificateSecurityService _certificateSecurity;

    public CertificateSchemaMigrationService(ECertDbContext db, CertificateSecurityService certificateSecurity)
    {
        _db = db;
        _certificateSecurity = certificateSecurity;
    }

    public async Task EnsureAsync()
    {
        await EnsureColumnAsync("PublicId", "VARCHAR(32) NULL");
        await EnsureColumnAsync("Status", "VARCHAR(20) NOT NULL DEFAULT 'Valid'");
        await EnsureColumnAsync("RevokedAt", "DATETIME NULL");
        await EnsureColumnAsync("RevokedReason", "VARCHAR(500) NULL");
        await EnsureColumnAsync("SignatureVersion", "INT NOT NULL DEFAULT 1");

        await EnsureColumnTypeAsync("CertificateNumber", "VARCHAR(40) NULL");
        await EnsureColumnTypeAsync("PublicId", "VARCHAR(32) NULL");
        await EnsureColumnTypeAsync("VerificationCode", "VARCHAR(24) NULL");
        await EnsureColumnTypeAsync("Status", "VARCHAR(20) NOT NULL DEFAULT 'Valid'");

        await NormalizeCertificateDataAsync();

        await EnsureUniqueIndexAsync("IX_Certificates_CertificateNumber", "CertificateNumber");
        await EnsureUniqueIndexAsync("IX_Certificates_PublicId", "PublicId");
        await EnsureUniqueIndexAsync("IX_Certificates_VerificationCode", "VerificationCode");
    }

    private async Task EnsureColumnAsync(string columnName, string definitionSql)
    {
        if (await ColumnExistsAsync(columnName))
            return;

        await _db.Database.ExecuteSqlRawAsync($"ALTER TABLE `Certificates` ADD COLUMN `{columnName}` {definitionSql};");
    }

    private async Task<bool> ColumnExistsAsync(string columnName)
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
  AND TABLE_NAME = 'Certificates'
  AND COLUMN_NAME = @columnName;";

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@columnName";
            parameter.Value = columnName;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result) > 0;
        }
        finally
        {
            if (shouldCloseConnection && connection.State == ConnectionState.Open)
                await connection.CloseAsync();
        }
    }

    private async Task<bool> IndexExistsAsync(string indexName)
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
FROM INFORMATION_SCHEMA.STATISTICS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'Certificates'
  AND INDEX_NAME = @indexName;";

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@indexName";
            parameter.Value = indexName;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result) > 0;
        }
        finally
        {
            if (shouldCloseConnection && connection.State == ConnectionState.Open)
                await connection.CloseAsync();
        }
    }

    private async Task EnsureColumnTypeAsync(string columnName, string definitionSql)
    {
        if (!await ColumnExistsAsync(columnName))
            return;

        await _db.Database.ExecuteSqlRawAsync($"ALTER TABLE `Certificates` MODIFY COLUMN `{columnName}` {definitionSql};");
    }

    private async Task EnsureUniqueIndexAsync(string indexName, string columnName)
    {
        if (await IndexExistsAsync(indexName))
            return;

        await _db.Database.ExecuteSqlRawAsync($"CREATE UNIQUE INDEX `{indexName}` ON `Certificates`(`{columnName}`);");
    }

    private async Task NormalizeCertificateDataAsync()
    {
        var certificates = await _db.Certificates.OrderBy(c => c.CertificateId).ToListAsync();
        var changed = false;

        foreach (var certificate in certificates)
        {
            if (string.IsNullOrWhiteSpace(certificate.CertificateNumber))
            {
                certificate.CertificateNumber = await GenerateUniqueAsync(c => c.CertificateNumber, () => _certificateSecurity.GenerateCertificateNumber(), certificates, certificate.CertificateId);
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(certificate.PublicId))
            {
                certificate.PublicId = await GenerateUniqueAsync(c => c.PublicId, () => _certificateSecurity.GeneratePublicId(), certificates, certificate.CertificateId);
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(certificate.VerificationCode))
            {
                certificate.VerificationCode = await GenerateUniqueAsync(c => c.VerificationCode, () => _certificateSecurity.GenerateVerificationCode(), certificates, certificate.CertificateId);
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(certificate.Status))
            {
                certificate.Status = "Valid";
                changed = true;
            }

            if (certificate.SignatureVersion <= 0)
            {
                certificate.SignatureVersion = 1;
                changed = true;
            }
        }

        changed |= await ResolveDuplicatesAsync(certificates, c => c.CertificateNumber, () => _certificateSecurity.GenerateCertificateNumber(), (c, value) => c.CertificateNumber = value);
        changed |= await ResolveDuplicatesAsync(certificates, c => c.PublicId, () => _certificateSecurity.GeneratePublicId(), (c, value) => c.PublicId = value);
        changed |= await ResolveDuplicatesAsync(certificates, c => c.VerificationCode, () => _certificateSecurity.GenerateVerificationCode(), (c, value) => c.VerificationCode = value);

        if (changed)
            await _db.SaveChangesAsync();
    }

    private async Task<bool> ResolveDuplicatesAsync(
        List<Certificate> certificates,
        Func<Certificate, string> selector,
        Func<string> generator,
        Action<Certificate, string> assign)
    {
        var changed = false;
        var duplicates = certificates
            .Where(c => !string.IsNullOrWhiteSpace(selector(c)))
            .GroupBy(selector, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in duplicates)
        {
            foreach (var duplicate in group.OrderBy(c => c.CertificateId).Skip(1))
            {
                var newValue = await GenerateUniqueAsync(selector, generator, certificates, duplicate.CertificateId);
                assign(duplicate, newValue);
                changed = true;
            }
        }

        return changed;
    }

    private Task<string> GenerateUniqueAsync(
        Func<Certificate, string> selector,
        Func<string> generator,
        List<Certificate> certificates,
        int currentCertificateId)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var candidate = generator();
            var existsInMemory = certificates.Any(c => c.CertificateId != currentCertificateId && string.Equals(selector(c), candidate, StringComparison.OrdinalIgnoreCase));
            if (!existsInMemory)
                return Task.FromResult(candidate);
        }

        throw new InvalidOperationException("تعذر توليد قيمة فريدة لحقول الشهادة.");
    }
}
