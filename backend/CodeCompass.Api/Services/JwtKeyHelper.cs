using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CodeCompass.Api.Services;

public static class JwtKeyHelper
{
    private static readonly Lazy<byte[]> FallbackKey = new(() =>
    {
        var key = new byte[64];
        RandomNumberGenerator.Fill(key);
        return key;
    });

    public static SymmetricSecurityKey GetSecurityKey(IConfiguration config)
    {
        var keyStr = config["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(keyStr))
        {
            keyStr = config["Jwt__Key"]
                  ?? Environment.GetEnvironmentVariable("Jwt__Key")
                  ?? Environment.GetEnvironmentVariable("Jwt__Key", EnvironmentVariableTarget.User)
                  ?? Environment.GetEnvironmentVariable("Jwt__Key", EnvironmentVariableTarget.Machine);
        }

        if (!string.IsNullOrWhiteSpace(keyStr) && keyStr.Length >= 32)
        {
            return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyStr));
        }

        // Ephemeral in-memory cryptographically secure key for process lifetime when no env var is configured
        return new SymmetricSecurityKey(FallbackKey.Value);
    }

    public static string GetIssuer(IConfiguration config)
    {
        return config["Jwt:Issuer"]
            ?? config["Jwt__Issuer"]
            ?? Environment.GetEnvironmentVariable("Jwt__Issuer")
            ?? "CodeCompass";
    }

    public static string GetAudience(IConfiguration config)
    {
        return config["Jwt:Audience"]
            ?? config["Jwt__Audience"]
            ?? Environment.GetEnvironmentVariable("Jwt__Audience")
            ?? "CodeCompassUsers";
    }
}
