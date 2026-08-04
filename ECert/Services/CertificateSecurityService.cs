using System.Security.Cryptography;
using System.Text;

namespace ECert.Services;

public class CertificateSecurityService
{
    private const string SafeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly byte[] _secretKey;

    public CertificateSecurityService(IConfiguration configuration)
    {
        var configuredSecret = configuration["CERT_HMAC_SECRET"];
        if (string.IsNullOrWhiteSpace(configuredSecret))
        {
            configuredSecret = configuration["DATABASE_URL"]
                ?? configuration.GetConnectionString("MySqlConnection")
                ?? "ECert-Fallback-Certificate-Secret";
        }

        _secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(configuredSecret));
    }

    public string GenerateCertificateNumber()
        => $"CERT-{DateTime.UtcNow.Year}-{GenerateSecureToken(10)}";

    public string GeneratePublicId()
        => GenerateSecureToken(16);

    public string GenerateVerificationCode()
        => GenerateSecureToken(12);

    public string GenerateSecureToken(int length)
    {
        var buffer = new byte[length];
        RandomNumberGenerator.Fill(buffer);

        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = SafeAlphabet[buffer[i] % SafeAlphabet.Length];
        }

        return new string(chars);
    }

    public string ComputeSignature(string publicId)
    {
        using var hmac = new HMACSHA256(_secretKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(publicId.Trim()));
        return ToBase64Url(hash.Take(16).ToArray());
    }

    public bool VerifySignature(string publicId, string? signature)
    {
        if (string.IsNullOrWhiteSpace(publicId) || string.IsNullOrWhiteSpace(signature))
            return false;

        var expected = Encoding.UTF8.GetBytes(ComputeSignature(publicId));
        var provided = Encoding.UTF8.GetBytes(signature.Trim());

        return expected.Length == provided.Length && CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    public string BuildVerificationUrl(string publicId, string baseUrl)
    {
        var normalizedBaseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        var safePublicId = Uri.EscapeDataString(publicId.Trim());
        var signature = Uri.EscapeDataString(ComputeSignature(publicId));
        return $"{normalizedBaseUrl}/v/{safePublicId}?sig={signature}";
    }

    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
