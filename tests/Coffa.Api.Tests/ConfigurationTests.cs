using Coffa.Api.Infrastructure;
using Npgsql;
using Xunit;
namespace Coffa.Api.Tests;
public class ConfigurationTests
{
    [Fact]
    public void RenderDatabaseUriIsParsedWithoutChangingEscapedCredentialsOrTlsMode()
    {
        var parsed=new NpgsqlConnectionStringBuilder(DatabaseConnection.Normalize("postgresql://test_user:test%3Ap%40ss@database.example:5439/coffa%5Fos?sslmode=verify-full"));
        Assert.Equal("database.example",parsed.Host);Assert.Equal(5439,parsed.Port);Assert.Equal("test_user",parsed.Username);Assert.Equal("test:p@ss",parsed.Password);Assert.Equal("coffa_os",parsed.Database);Assert.Equal(SslMode.VerifyFull,parsed.SslMode);
    }
    [Fact]
    public void NativeConnectionStringRemainsUnchanged()
    {const string input="Host=127.0.0.1;Port=55439;Database=test;Username=test";Assert.Equal(input,DatabaseConnection.Normalize(input));}
}
