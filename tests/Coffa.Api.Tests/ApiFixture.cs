using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coffa.Api.Data;
using Coffa.Api.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
namespace Coffa.Api.Tests;

[CollectionDefinition("api",DisableParallelization=true)]
public class ApiCollection : ICollectionFixture<ApiFixture>;

public class ApiFixture : IAsyncLifetime
{
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public string OutboxPath { get; } = Path.Combine(Path.GetTempPath(),"coffa-outbox-"+Guid.NewGuid().ToString("N"));
    public string Schema { get; } = "test_"+Guid.NewGuid().ToString("N");
    public string Connection { get; private set; } = "";
    public async Task InitializeAsync()
    {
        var connection=Environment.GetEnvironmentVariable("COFFA_TEST_CONNECTION");
        if(string.IsNullOrEmpty(connection))
        {
            var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory!=null&&!File.Exists(Path.Combine(directory.FullName,".env.coffa.local")))directory=directory.Parent;
            if(directory==null)throw new InvalidOperationException("Stel COFFA_TEST_CONNECTION in op een geïsoleerde PostgreSQL-testdatabase.");
            var values=File.ReadAllLines(Path.Combine(directory.FullName,".env.coffa.local")).Where(x=>!x.StartsWith('#')&&x.Contains('=')).Select(x=>x.Split('=',2)).ToDictionary(x=>x[0],x=>x[1]);
            connection=new NpgsqlConnectionStringBuilder{Host="127.0.0.1",Port=int.TryParse(values.GetValueOrDefault("COFFA_POSTGRES_PORT"),out var port)?port:55439,Database=values.GetValueOrDefault("POSTGRES_DB")??"coffa_os_dev",Username=values.GetValueOrDefault("POSTGRES_USER")??"coffa_os",Password=values["POSTGRES_PASSWORD"]}.ConnectionString;
        }
        await using(var admin=new NpgsqlConnection(connection)){await admin.OpenAsync();await using var command=new NpgsqlCommand($"CREATE SCHEMA \"{Schema}\"",admin);await command.ExecuteNonQueryAsync();}
        var builder=new NpgsqlConnectionStringBuilder(connection){SearchPath=Schema};Connection=builder.ConnectionString;
        var options=new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Connection).Options;
        await using(var db=new AppDbContext(options))await db.Database.MigrateAsync();
        Factory=new TestFactory(Connection,OutboxPath);
        using var client=CreateClient();await client.GetAsync("/api/auth/csrf");
    }
    public HttpClient CreateClient()=>Factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("http://localhost"),HandleCookies=true,AllowAutoRedirect=false});
    public async Task<HttpClient> AdminAsync()
    {
        var email="admin-"+Guid.NewGuid().ToString("N")+"@example.com";const string password="Integration-Password-42!";
        using(var scope=Factory.Services.CreateScope())
        {
            var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var user=new ApplicationUser{UserName=email,Email=email,EmailConfirmed=true,DisplayName="Test administrator"};
            Assert.True((await users.CreateAsync(user,password)).Succeeded);Assert.True((await users.AddToRoleAsync(user,"SuperAdmin")).Succeeded);
        }
        var client=CreateClient();var response=await SendAsync(client,HttpMethod.Post,"/api/auth/login",new{email,password});Assert.Equal(System.Net.HttpStatusCode.OK,response.StatusCode);
        var setup=await client.GetFromJsonAsync<JsonElement>("/api/auth/2fa/setup");var key=setup.GetProperty("sharedKey").GetString()!;
        response=await SendAsync(client,HttpMethod.Post,"/api/auth/2fa/enable",new{code=Totp(key)});Assert.Equal(System.Net.HttpStatusCode.OK,response.StatusCode);
        return client;
    }
    public static async Task<HttpResponseMessage> SendAsync(HttpClient client,HttpMethod method,string path,object? body=null)
    {
        var csrf=await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");using var request=new HttpRequestMessage(method,path);
        request.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());if(body!=null)request.Content=JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {var content=await response.Content.ReadAsStringAsync();Assert.True(response.IsSuccessStatusCode,$"HTTP {(int)response.StatusCode}: {content}");return JsonDocument.Parse(content).RootElement.Clone();}
    public async Task<string> InvitationTokenAsync(string outboxId)
    {
        var json=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(OutboxPath,outboxId+".json")));
        var html=json.RootElement.GetProperty("Message").GetProperty("HtmlBody").GetString()!;
        var match=System.Text.RegularExpressions.Regex.Match(html,@"token=([^""&<>]+)");Assert.True(match.Success);return Uri.UnescapeDataString(match.Groups[1].Value);
    }
    public static string Totp(string key)
    {
        const string alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";var bits=0;var value=0;var bytes=new List<byte>();foreach(var c in key.TrimEnd('=').ToUpperInvariant()){value=(value<<5)|alphabet.IndexOf(c);bits+=5;if(bits>=8){bytes.Add((byte)(value>>(bits-8)));bits-=8;}}
        var counter=DateTimeOffset.UtcNow.ToUnixTimeSeconds()/30;var input=BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(counter));var hash=HMACSHA1.HashData(bytes.ToArray(),input);var offset=hash[^1]&15;var binary=((hash[offset]&127)<<24)|((hash[offset+1]&255)<<16)|((hash[offset+2]&255)<<8)|(hash[offset+3]&255);return(binary%1000000).ToString("D6");
    }
    public async Task DisposeAsync()
    {
        if(Factory!=null)await Factory.DisposeAsync();
        if(!string.IsNullOrEmpty(Connection)){var original=new NpgsqlConnectionStringBuilder(Connection){SearchPath="public"};await using var connection=new NpgsqlConnection(original.ConnectionString);await connection.OpenAsync();await using var command=new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{Schema}\" CASCADE",connection);await command.ExecuteNonQueryAsync();}
        if(Directory.Exists(OutboxPath))Directory.Delete(OutboxPath,true);
    }
    private sealed class TestFactory(string connection,string outbox) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");builder.UseSetting("ConnectionStrings:Default",connection);builder.UseSetting("Email:OutboxPath",outbox);builder.UseSetting("Email:EnableSending","false");
            builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:Default",connection},{"Email:OutboxPath",outbox},{"Email:EnableSending","false"},{"Storage:DevelopmentDirectory",Path.Combine(outbox,"objects")},{"Bootstrap:Email",null},{"Bootstrap:Password",null}}));
        }
    }
}
