using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coffa.Api.Data;
using Coffa.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace Coffa.Api.Tests;

[Collection("api")]
public class AuthTests(ApiFixture fixture)
{
    [Fact]
    public async Task SuperAdminIsRestrictedUntilTotpEnabledAndRecoveryCodesAreSingleUse()
    {
        var email=$"setup-{Guid.NewGuid():N}@example.com";const string password="Integration-Password-42!";
        using(var scope=fixture.Factory.Services.CreateScope())
        {var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var user=new ApplicationUser{UserName=email,Email=email,EmailConfirmed=true,DisplayName="Admin"};Assert.True((await users.CreateAsync(user,password)).Succeeded);Assert.True((await users.AddToRoleAsync(user,"SuperAdmin")).Succeeded);}
        using var client=fixture.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/organizations")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/auth/login",new{email,password})).StatusCode);
        var login=await ApiFixture.JsonAsync(await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/auth/login",new{email,password}));Assert.True(login.GetProperty("requiresTwoFactorSetup").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/organizations")).StatusCode);
        var setup=await client.GetFromJsonAsync<JsonElement>("/api/auth/2fa/setup");var key=setup.GetProperty("sharedKey").GetString()!;
        var enabled=await ApiFixture.JsonAsync(await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/auth/2fa/enable",new{code=ApiFixture.Totp(key)}));var recovery=enabled.GetProperty("recoveryCodes")[0].GetString()!;
        Assert.False(enabled.GetProperty("user").GetProperty("requiresTwoFactorSetup").GetBoolean());Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/organizations")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/auth/login",new{email,password,twoFactorCode="invalid"})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/auth/login",new{email,password,recoveryCode=recovery})).StatusCode);
        await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/auth/logout");Assert.Equal(HttpStatusCode.Unauthorized,(await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/auth/login",new{email,password,recoveryCode=recovery})).StatusCode);
    }

    [Fact]
    public async Task InvitationVerifiesEmailEnforcesTenantIsolationAndDeactivationRevokesSessions()
    {
        using var admin=await fixture.AdminAsync();var first=await CreateOrganization(admin,"First organization");var second=await CreateOrganization(admin,"Second organization");var email=$"client-{Guid.NewGuid():N}@example.com";
        var invitation=await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin,HttpMethod.Post,"/api/invitations",new{email,organizationId=first,role="Customer",permissions=Array.Empty<string>()}));Assert.False(invitation.GetProperty("delivery").GetProperty("sent").GetBoolean());
        var token=await fixture.InvitationTokenAsync(invitation.GetProperty("delivery").GetProperty("outboxId").GetString()!);
        using(var scope=fixture.Factory.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var stored=await db.UserInvitations.SingleAsync(x=>x.Email==email);Assert.NotEqual(token,stored.TokenHash);Assert.Equal(64,stored.TokenHash.Length);}
        using var client=fixture.CreateClient();const string password="Customer-Password-42!";
        Assert.Equal(HttpStatusCode.OK,(await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/auth/accept-invitation",new{token,name="Customer",password})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/auth/accept-invitation",new{token,name="Again",password})).StatusCode);
        var me=await ApiFixture.JsonAsync(await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/auth/login",new{email,password}));var userId=me.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync($"/api/organizations/{first}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/organizations/{second}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/invitations")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await ApiFixture.SendAsync(admin,HttpMethod.Put,$"/api/users/{userId}",new{name="Customer",role="Customer",organizationId=first,permissions=Array.Empty<string>(),isActive=false})).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task CustomerInvitationsRequireOrganizationAndShareOneCustomerWithoutAcceptPayloadOverride()
    {
        using var admin=await fixture.AdminAsync();
        var first=await CreateOrganization(admin,"Shared portal organization");
        var second=await CreateOrganization(admin,"Unrelated portal organization");
        using var anonymous=fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync($"/api/users?organizationId={first}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync($"/api/invitations?organizationId={first}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await ApiFixture.SendAsync(admin,HttpMethod.Post,"/api/invitations",new{email=$"missing-org-{Guid.NewGuid():N}@example.com",role="Customer"})).StatusCode);
        var outsidersInvite=await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin,HttpMethod.Post,"/api/invitations",new{email=$"outsider-{Guid.NewGuid():N}@example.com",organizationId=second,role="Customer"}));
        for(var index=0;index<2;index++)
        {
            var email=$"same-org-{Guid.NewGuid():N}@example.com";
            var invitation=await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin,HttpMethod.Post,"/api/invitations",new{email,organizationId=first,role="Customer"}));
            var token=await fixture.InvitationTokenAsync(invitation.GetProperty("delivery").GetProperty("outboxId").GetString()!);
            using var client=fixture.CreateClient();
            const string password="Multiuser-Customer-42!";
            // Unknown JSON fields cannot replace the organization and role bound to the invitation token.
            await ApiFixture.JsonAsync(await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/auth/accept-invitation",new{token,name=$"Contact {index}",password,organizationId=second,role="SuperAdmin"}));
            var me=await ApiFixture.JsonAsync(await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/auth/login",new{email,password}));
            Assert.Equal(first,me.GetProperty("organizationId").GetGuid());
            Assert.Equal("Customer",me.GetProperty("role").GetString());
            Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/organizations/{second}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/api/users?organizationId={first}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/api/invitations?organizationId={first}")).StatusCode);
        }
        var people=await ApiFixture.JsonAsync(await admin.GetAsync($"/api/users?organizationId={first}"));
        Assert.Equal(2,people.GetArrayLength());
        Assert.All(people.EnumerateArray(),person=>Assert.Equal(first,person.GetProperty("organizationId").GetGuid()));
        var invitations=await ApiFixture.JsonAsync(await admin.GetAsync($"/api/invitations?organizationId={first}"));
        Assert.Equal(2,invitations.GetArrayLength());
        Assert.All(invitations.EnumerateArray(),invitation=>Assert.Equal(first,invitation.GetProperty("organizationId").GetGuid()));
        Assert.DoesNotContain(outsidersInvite.GetProperty("id").GetGuid().ToString(),invitations.GetRawText());
    }

    [Fact]
    public async Task RevokedAndExpiredInvitationsCannotBeAcceptedAndResendRotatesToken()
    {
        using var admin=await fixture.AdminAsync();var organization=await CreateOrganization(admin,"Invitation checks");var email=$"invite-{Guid.NewGuid():N}@example.com";
        var invitation=await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin,HttpMethod.Post,"/api/invitations",new{email,organizationId=organization,role="Customer"}));var id=invitation.GetProperty("id").GetGuid();var oldToken=await fixture.InvitationTokenAsync(invitation.GetProperty("delivery").GetProperty("outboxId").GetString()!);
        await ApiFixture.SendAsync(admin,HttpMethod.Delete,$"/api/invitations/{id}");using var client=fixture.CreateClient();Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync($"/api/auth/invitations/{oldToken}")).StatusCode);
        var resent=await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin,HttpMethod.Post,$"/api/invitations/{id}/resend"));var newToken=await fixture.InvitationTokenAsync(resent.GetProperty("delivery").GetProperty("outboxId").GetString()!);Assert.NotEqual(oldToken,newToken);Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync($"/api/auth/invitations/{oldToken}")).StatusCode);
        using(var scope=fixture.Factory.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var stored=await db.UserInvitations.FindAsync(id);stored!.ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(-1);await db.SaveChangesAsync();}
        Assert.Equal(HttpStatusCode.BadRequest,(await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/auth/accept-invitation",new{token=newToken,name="Customer",password="Customer-Password-42!"})).StatusCode);
    }

    [Fact]
    public async Task PasswordResetResponseIsGenericAndResetRevokesEveryExistingSession()
    {
        var email=$"reset-{Guid.NewGuid():N}@example.com";const string password="Original-Password-42!";
        await SeedUser(email,password);
        using var first=fixture.CreateClient();using var second=fixture.CreateClient();
        Assert.Equal(HttpStatusCode.OK,(await ApiFixture.SendAsync(first,HttpMethod.Post,"/api/auth/login",new{email,password})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await ApiFixture.SendAsync(second,HttpMethod.Post,"/api/auth/login",new{email,password})).StatusCode);
        var files=Directory.GetFiles(fixture.OutboxPath).ToHashSet();
        using var anonymous=fixture.CreateClient();
        var known=await ApiFixture.JsonAsync(await ApiFixture.SendAsync(anonymous,HttpMethod.Post,"/api/auth/forgot-password",new{email}));
        var unknown=await ApiFixture.JsonAsync(await ApiFixture.SendAsync(anonymous,HttpMethod.Post,"/api/auth/forgot-password",new{email=$"unknown-{Guid.NewGuid():N}@example.com"}));
        Assert.Equal(known.GetRawText(),unknown.GetRawText());Assert.False(known.TryGetProperty("delivery",out _));
        var newFile=Directory.GetFiles(fixture.OutboxPath).Single(x=>!files.Contains(x));var token=await fixture.InvitationTokenAsync(Path.GetFileNameWithoutExtension(newFile));
        const string newPassword="Replaced-Password-42!";
        Assert.Equal(HttpStatusCode.NoContent,(await ApiFixture.SendAsync(anonymous,HttpMethod.Post,"/api/auth/reset-password",new{email,token,newPassword})).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await first.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await second.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await ApiFixture.SendAsync(anonymous,HttpMethod.Post,"/api/auth/reset-password",new{email,token,newPassword})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await ApiFixture.SendAsync(anonymous,HttpMethod.Post,"/api/auth/login",new{email,password=newPassword})).StatusCode);
    }

    [Fact]
    public async Task CsrfIsIdentityBoundAndLogoutAllRevokesOtherCookies()
    {
        var email=$"sessions-{Guid.NewGuid():N}@example.com";const string password="Sessions-Password-42!";await SeedUser(email,password);
        using var first=fixture.CreateClient();using var second=fixture.CreateClient();
        var beforeLogin=await first.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        var login=await ApiFixture.SendAsync(first,HttpMethod.Post,"/api/auth/login",new{email,password});Assert.Equal(HttpStatusCode.OK,login.StatusCode);
        var cookie=string.Join(";",login.Headers.GetValues("Set-Cookie"));Assert.Contains("httponly",cookie,StringComparison.OrdinalIgnoreCase);Assert.Contains("samesite=strict",cookie,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK,(await ApiFixture.SendAsync(second,HttpMethod.Post,"/api/auth/login",new{email,password})).StatusCode);
        using(var stale=new HttpRequestMessage(HttpMethod.Post,"/api/auth/logout")){stale.Headers.Add("X-CSRF-TOKEN",beforeLogin.GetProperty("token").GetString());Assert.Equal(HttpStatusCode.BadRequest,(await first.SendAsync(stale)).StatusCode);}
        Assert.Equal(HttpStatusCode.OK,(await first.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await ApiFixture.SendAsync(first,HttpMethod.Post,"/api/auth/logout-all")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await second.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task UnverifiedAccountRequiresVerificationAndPasswordFailuresLockAccount()
    {
        var email=$"verify-{Guid.NewGuid():N}@example.com";const string password="Verification-Password-42!";await SeedUser(email,password,false);
        using var client=fixture.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/auth/login",new{email,password})).StatusCode);
        var files=Directory.GetFiles(fixture.OutboxPath).ToHashSet();
        await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/auth/verification/request",new{email});
        var newFile=Directory.GetFiles(fixture.OutboxPath).Single(x=>!files.Contains(x));var token=await fixture.InvitationTokenAsync(Path.GetFileNameWithoutExtension(newFile));
        Assert.Equal(HttpStatusCode.NoContent,(await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/auth/verification/confirm",new{email,token})).StatusCode);
        for(var attempt=0;attempt<5;attempt++)Assert.Equal(HttpStatusCode.Unauthorized,(await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/auth/login",new{email,password="Wrong-Password-42!"})).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/auth/login",new{email,password})).StatusCode);
        using var scope=fixture.Factory.Services.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var user=await users.FindByEmailAsync(email);Assert.True(await users.IsLockedOutAsync(user!));Assert.True(user!.EmailConfirmed);
    }

    private async Task SeedUser(string email,string password,bool confirmed=true)
    {using var scope=fixture.Factory.Services.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var user=new ApplicationUser{UserName=email,Email=email,EmailConfirmed=confirmed,DisplayName="Employee"};Assert.True((await users.CreateAsync(user,password)).Succeeded);Assert.True((await users.AddToRoleAsync(user,"Employee")).Succeeded);}

    [Fact]
    public async Task BootstrapCreatesOnlyFirstAdminAndNeverReplacesAnExistingPassword()
    {
        var isolated=new ApiFixture();await isolated.InitializeAsync();
        try
        {
            var config=new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Bootstrap:Email","first-admin@example.com"},{"Bootstrap:Password","Bootstrap-Password-42!"}}).Build();
            await Coffa.Api.Infrastructure.Bootstrap.InitializeAsync(isolated.Factory.Services,config);
            await Coffa.Api.Infrastructure.Bootstrap.InitializeAsync(isolated.Factory.Services,new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Bootstrap:Email","different-admin@example.com"},{"Bootstrap:Password","Changed-Password-42!"}}).Build());
            using var scope=isolated.Factory.Services.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var admins=await users.GetUsersInRoleAsync("SuperAdmin");Assert.Single(admins);Assert.Equal("first-admin@example.com",admins[0].Email);Assert.False(admins[0].TwoFactorEnabled);Assert.True(await users.CheckPasswordAsync(admins[0],"Bootstrap-Password-42!"));
        }
        finally{await isolated.DisposeAsync();}
    }

    private static async Task<Guid> CreateOrganization(HttpClient client,string name)
    {var organization=await ApiFixture.JsonAsync(await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/organizations",new{name,email="company@example.com",phone="",website="",address="",chamberOfCommerce="",vatNumber="",type="customer",status="active",tags=Array.Empty<string>(),internalNotes="Private staff notes"}));return organization.GetProperty("id").GetGuid();}
}
