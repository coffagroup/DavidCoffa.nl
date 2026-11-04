using System.Security.Claims;
using Coffa.Api.Data;
using Coffa.Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace Coffa.Api.Infrastructure;

public record CurrentUserDto(string Id,string Email,string Name,string Role,Guid? OrganizationId,string[] Permissions,bool RequiresTwoFactorSetup,bool TwoFactorEnabled);
public sealed class AuthService(AppDbContext db,UserManager<ApplicationUser> users,SignInManager<ApplicationUser> signIn,IHttpContextAccessor accessor)
{
    public async Task<CurrentUserDto> DescribeAsync(ApplicationUser user,bool setup=false)
    {var roles=await users.GetRolesAsync(user);var role=roles.FirstOrDefault()??"Customer";var permissions=role=="SuperAdmin"?Infrastructure.Permissions.All:(await users.GetClaimsAsync(user)).Where(x=>x.Type=="permission").Select(x=>x.Value).ToArray();return new(user.Id,user.Email!,user.DisplayName,role,user.OrganizationId,permissions,setup,user.TwoFactorEnabled);}
    public async Task<CurrentUserDto> CreateSessionAsync(ApplicationUser user,bool setupOnly=false)
    {
        var context=accessor.HttpContext!;var principal=await signIn.CreateUserPrincipalAsync(user);var identity=(ClaimsIdentity)principal.Identity!;
        var session=new UserSession{UserId=user.Id,Version=user.SessionVersion,SetupOnly=setupOnly,ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(setupOnly?20:480),UserAgent=(context.Request.Headers.UserAgent.ToString())[..Math.Min(300,context.Request.Headers.UserAgent.ToString().Length)]};
        db.UserSessions.Add(session);await db.SaveChangesAsync();
        identity.AddClaim(new("session_id",session.Id.ToString()));identity.AddClaim(new("session_kind",setupOnly?"setup":"full"));
        if(user.OrganizationId is Guid org)identity.AddClaim(new("organization_id",org.ToString()));
        await context.SignInAsync(IdentityConstants.ApplicationScheme,principal,new AuthenticationProperties{ExpiresUtc=session.ExpiresAt,IsPersistent=false,AllowRefresh=false});
        return await DescribeAsync(user,setupOnly);
    }
    public async Task RevokeAllAsync(ApplicationUser user)
    {user.SessionVersion++;Validation.Identity(await users.UpdateAsync(user));await db.UserSessions.Where(x=>x.UserId==user.Id&&x.RevokedAt==null).ExecuteUpdateAsync(x=>x.SetProperty(s=>s.RevokedAt,DateTimeOffset.UtcNow));}
    public async Task RevokeCurrentAsync()
    {if(Guid.TryParse(accessor.HttpContext?.User.FindFirstValue("session_id"),out var id))await db.UserSessions.Where(x=>x.Id==id).ExecuteUpdateAsync(x=>x.SetProperty(s=>s.RevokedAt,DateTimeOffset.UtcNow));await accessor.HttpContext!.SignOutAsync(IdentityConstants.ApplicationScheme);}
}
