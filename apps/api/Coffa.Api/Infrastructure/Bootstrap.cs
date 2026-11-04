using Coffa.Api.Domain;
using Coffa.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace Coffa.Api.Infrastructure;
public static class Bootstrap
{
    public static async Task InitializeAsync(IServiceProvider services,IConfiguration config)
    {
        using var scope=services.CreateScope();var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1842406319)");
        foreach(var name in new[]{"SuperAdmin","Employee","Customer"})if(!await roles.RoleExistsAsync(name))Validation.Identity(await roles.CreateAsync(new IdentityRole(name)));
        var email=config["Bootstrap:Email"];var password=config["Bootstrap:Password"];
        if(string.IsNullOrWhiteSpace(email)||string.IsNullOrWhiteSpace(password)){await transaction.CommitAsync();return;}
        if((await users.GetUsersInRoleAsync("SuperAdmin")).Count>0){await transaction.CommitAsync();return;}
        if(await users.FindByEmailAsync(email)!=null)throw new InvalidOperationException("Bootstrap e-mail is al in gebruik door een ander account.");
        var user=new ApplicationUser{UserName=email,Email=email,EmailConfirmed=true,DisplayName=config["Bootstrap:Name"]??"David Coffa"};Validation.Identity(await users.CreateAsync(user,password));Validation.Identity(await users.AddToRoleAsync(user,"SuperAdmin"));await transaction.CommitAsync();
    }
}
