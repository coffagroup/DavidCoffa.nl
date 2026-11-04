using Microsoft.AspNetCore.WebUtilities;
using Npgsql;
namespace Coffa.Api.Infrastructure;
public static class DatabaseConnection
{
    public static string Normalize(string connection)
    {
        if(!connection.StartsWith("postgres://",StringComparison.OrdinalIgnoreCase)&&!connection.StartsWith("postgresql://",StringComparison.OrdinalIgnoreCase))return connection;
        var uri=new Uri(connection);var credentials=uri.UserInfo.Split(':',2);
        var builder=new NpgsqlConnectionStringBuilder{Host=uri.Host,Port=uri.Port>0?uri.Port:5432,Username=Uri.UnescapeDataString(credentials[0]),Password=credentials.Length>1?Uri.UnescapeDataString(credentials[1]):"",Database=Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'))};
        var query=QueryHelpers.ParseQuery(uri.Query);
        if(query.TryGetValue("sslmode",out var mode))builder.SslMode=mode.ToString().ToLowerInvariant() switch{"disable"=>SslMode.Disable,"allow"=>SslMode.Allow,"prefer"=>SslMode.Prefer,"require"=>SslMode.Require,"verify-ca"=>SslMode.VerifyCA,"verify-full"=>SslMode.VerifyFull,_=>throw new InvalidOperationException("Onbekende PostgreSQL TLS-instelling.")};
        return builder.ConnectionString;
    }
}
