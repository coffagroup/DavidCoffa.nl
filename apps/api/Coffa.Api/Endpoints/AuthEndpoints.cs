using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Coffa.Api.Data;
using Coffa.Api.Domain;
using Coffa.Api.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
namespace Coffa.Api.Endpoints;

public static class AuthEndpoints
{
    public record LoginRequest(string Email,string Password,string? TwoFactorCode,string? RecoveryCode);
    public record CodeRequest(string Code);
    public record PasswordRequest(string CurrentPassword,string NewPassword);
    public record EmailRequest(string Email);
    public record ResetRequest(string Email,string Token,string NewPassword);
    public record VerifyEmailRequest(string Email,string Token);
    public record AcceptRequest(string Token,string Name,string Password);
    public record InvitationRequest(string Email,Guid? OrganizationId,string? Role,string[]? Permissions);
    public record UpdateUserRequest(string Name,string Role,Guid? OrganizationId,string[] Permissions,bool IsActive);

    public static void MapAuth(this WebApplication app)
    {
        var group=app.MapGroup("/api/auth").WithTags("Accounts");
        group.MapGet("/csrf",(IAntiforgery antiforgery,HttpContext context)=>{context.Response.Headers.CacheControl="no-cache, no-store";context.Response.Headers.Pragma="no-cache";return Results.Ok(new{token=antiforgery.GetAndStoreTokens(context).RequestToken});}).AllowAnonymous();
        group.MapPost("/login",async(LoginRequest request,UserManager<ApplicationUser> users,SignInManager<ApplicationUser> signIn,AuthService auth,AuditService audit)=>{
            var user=await users.FindByEmailAsync(request.Email?.Trim()??"");
            if(user is null||!user.IsActive)throw InvalidLogin();
            var check=await signIn.CheckPasswordSignInAsync(user,request.Password??"",true);
            if(!check.Succeeded)throw InvalidLogin();
            if(!user.EmailConfirmed)throw new ApiException(403,"email_confirmation_required","Verifieer eerst je e-mailadres.");
            var isAdmin=await users.IsInRoleAsync(user,"SuperAdmin");
            if(user.TwoFactorEnabled)
            {
                if(string.IsNullOrWhiteSpace(request.TwoFactorCode)&&string.IsNullOrWhiteSpace(request.RecoveryCode))throw new ApiException(401,"two_factor_required","Vul je authenticatorcode of een recoverycode in.");
                var verified=!string.IsNullOrWhiteSpace(request.RecoveryCode)
                    ?(await users.RedeemTwoFactorRecoveryCodeAsync(user,request.RecoveryCode.Replace(" ",""))).Succeeded
                    :await users.VerifyTwoFactorTokenAsync(user,TokenOptions.DefaultAuthenticatorProvider,NormalizeCode(request.TwoFactorCode!));
                if(!verified){await users.AccessFailedAsync(user);throw InvalidLogin();}
            }
            await users.ResetAccessFailedCountAsync(user);
            var me=await auth.CreateSessionAsync(user,isAdmin&&!user.TwoFactorEnabled);
            await audit.RecordAsync("account.login","user",user.Id);
            return Results.Ok(me);
        }).AllowAnonymous().RequireRateLimiting("auth");
        group.MapGet("/me",async(HttpContext context,UserManager<ApplicationUser> users,AuthService auth)=>{
            var user=await users.GetUserAsync(context.User)??throw new ApiException(401,"unauthenticated","Log eerst in.");
            return Results.Ok(await auth.DescribeAsync(user,context.User.FindFirstValue("session_kind")=="setup"));
        }).RequireAuthorization("SetupOrFull");
        group.MapPost("/logout",async(AuthService auth,AuditService audit)=>{await audit.RecordAsync("account.logout","session");await auth.RevokeCurrentAsync();return Results.NoContent();}).RequireAuthorization("SetupOrFull");
        group.MapPost("/logout-all",async(HttpContext context,UserManager<ApplicationUser> users,AuthService auth,AuditService audit)=>{var user=await CurrentUser(context,users);await auth.RevokeAllAsync(user);await audit.RecordAsync("account.sessions_revoked","user",user.Id);await auth.RevokeCurrentAsync();return Results.NoContent();}).RequireAuthorization();
        group.MapGet("/sessions",async(AppDbContext db,CurrentActor actor,HttpContext context)=>await db.UserSessions.AsNoTracking().Where(x=>x.UserId==actor.UserId&&x.RevokedAt==null&&x.ExpiresAt>DateTimeOffset.UtcNow).OrderByDescending(x=>x.CreatedAt).Select(x=>new{x.Id,x.CreatedAt,x.ExpiresAt,x.UserAgent,isCurrent=x.Id.ToString()==context.User.FindFirstValue("session_id")}).ToListAsync()).RequireAuthorization();
        group.MapDelete("/sessions/{id:guid}",async(Guid id,AppDbContext db,CurrentActor actor,AuthService auth,HttpContext context)=>{var session=await db.UserSessions.SingleOrDefaultAsync(x=>x.Id==id&&x.UserId==actor.UserId)??throw NotFound();session.RevokedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync();if(context.User.FindFirstValue("session_id")==id.ToString())await auth.RevokeCurrentAsync();return Results.NoContent();}).RequireAuthorization();
        group.MapGet("/2fa/setup",async(HttpContext context,UserManager<ApplicationUser> users,AuthService auth)=>{
            var user=await CurrentUser(context,users);if(user.TwoFactorEnabled)throw new ApiException(409,"two_factor_enabled","Tweestapsverificatie is al ingeschakeld.");
            var key=await users.GetAuthenticatorKeyAsync(user);if(string.IsNullOrEmpty(key)){Validation.Identity(await users.ResetAuthenticatorKeyAsync(user));key=await users.GetAuthenticatorKeyAsync(user);var setupOnly=context.User.FindFirstValue("session_kind")=="setup";await auth.RevokeCurrentAsync();await auth.CreateSessionAsync(user,setupOnly);}
            var issuer=Uri.EscapeDataString("Coffa OS");var email=Uri.EscapeDataString(user.Email!);
            return Results.Ok(new{sharedKey=key,authenticatorUri=$"otpauth://totp/{issuer}:{email}?secret={key}&issuer={issuer}&digits=6"});
        }).RequireAuthorization("SetupOrFull");
        group.MapPost("/2fa/enable",async(CodeRequest request,HttpContext context,UserManager<ApplicationUser> users,AuthService auth,AuditService audit)=>{
            var user=await CurrentUser(context,users);if(user.TwoFactorEnabled)throw new ApiException(409,"two_factor_enabled","Tweestapsverificatie is al ingeschakeld.");
            if(!await users.VerifyTwoFactorTokenAsync(user,TokenOptions.DefaultAuthenticatorProvider,NormalizeCode(request.Code??"")))throw new ApiException(400,"invalid_code","De authenticatorcode klopt niet.");
            Validation.Identity(await users.SetTwoFactorEnabledAsync(user,true));var codes=(await users.GenerateNewTwoFactorRecoveryCodesAsync(user,10))!.ToArray();await auth.RevokeAllAsync(user);await auth.RevokeCurrentAsync();var me=await auth.CreateSessionAsync(user);await audit.RecordAsync("account.two_factor_enabled","user",user.Id);return Results.Ok(new{recoveryCodes=codes,user=me});
        }).RequireAuthorization("SetupOrFull").RequireRateLimiting("auth");
        group.MapPost("/2fa/recovery-codes",async(CodeRequest request,HttpContext context,UserManager<ApplicationUser> users,AuditService audit)=>{var user=await CurrentUser(context,users);if(!user.TwoFactorEnabled||!await users.VerifyTwoFactorTokenAsync(user,TokenOptions.DefaultAuthenticatorProvider,NormalizeCode(request.Code??"")))throw new ApiException(400,"invalid_code","De authenticatorcode klopt niet.");var codes=await users.GenerateNewTwoFactorRecoveryCodesAsync(user,10);await audit.RecordAsync("account.recovery_codes_regenerated","user",user.Id);return Results.Ok(new{recoveryCodes=codes});}).RequireAuthorization().RequireRateLimiting("auth");
        group.MapPost("/password/change",async(PasswordRequest request,HttpContext context,UserManager<ApplicationUser> users,AuthService auth,AuditService audit)=>{var user=await CurrentUser(context,users);Validation.Identity(await users.ChangePasswordAsync(user,request.CurrentPassword,request.NewPassword));await auth.RevokeAllAsync(user);await audit.RecordAsync("account.password_changed","user",user.Id);await auth.RevokeCurrentAsync();return Results.NoContent();}).RequireAuthorization().RequireRateLimiting("auth");
        group.MapPost("/forgot-password",async(EmailRequest request,UserManager<ApplicationUser> users,IEmailService email,IConfiguration config,ILogger<AuthService> logger)=>{
            var user=await users.FindByEmailAsync(request.Email?.Trim()??"");
            if(user is not null&&user.IsActive&&user.EmailConfirmed)
            {
                var token=Encode(await users.GeneratePasswordResetTokenAsync(user));var link=$"{PortalUrl(config)}/reset?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";
                try{await email.SendAsync(user.Email!,"Stel je Coffa OS-wachtwoord opnieuw in",$"<p>Gebruik deze persoonlijke link om je wachtwoord opnieuw in te stellen:</p><p><a href=\"{HtmlEncoder.Default.Encode(link)}\">Wachtwoord opnieuw instellen</a></p>");}
                catch{logger.LogWarning("Password reset delivery unavailable; check mail configuration");}
            }
            return Results.Ok(new{message="Resetverzoek verwerkt. Als het account bestaat en e-mailverzending beschikbaar is, ontvang je een persoonlijke link."});
        }).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/reset-password",async(ResetRequest request,UserManager<ApplicationUser> users,AuthService auth,AuditService audit)=>{var user=await users.FindByEmailAsync(request.Email?.Trim()??"");if(user is null)throw new ApiException(400,"invalid_token","Deze resetlink is ongeldig of verlopen.");Validation.Identity(await users.ResetPasswordAsync(user,Decode(request.Token),request.NewPassword));await auth.RevokeAllAsync(user);await audit.RecordAsync("account.password_reset","user",user.Id);return Results.NoContent();}).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/verification/request",async(EmailRequest request,UserManager<ApplicationUser> users,IEmailService email,IConfiguration config,ILogger<AuthService> logger)=>{
            var user=await users.FindByEmailAsync(request.Email?.Trim()??"");
            if(user is not null&&user.IsActive&&!user.EmailConfirmed)
            {
                var token=Encode(await users.GenerateEmailConfirmationTokenAsync(user));var link=$"{PortalUrl(config)}/verifieer?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";
                try{await email.SendAsync(user.Email!,"Verifieer je e-mailadres",$"<a href=\"{HtmlEncoder.Default.Encode(link)}\">Verifieer je e-mailadres</a>");}
                catch{logger.LogWarning("Verification delivery unavailable; check mail configuration");}
            }
            return Results.Ok(new{message="Verzoek verwerkt. Als verificatie nodig is en e-mailverzending beschikbaar is, ontvang je een persoonlijke link."});
        }).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/verification/confirm",async(VerifyEmailRequest request,UserManager<ApplicationUser> users)=>{var user=await users.FindByEmailAsync(request.Email?.Trim()??"")??throw new ApiException(400,"invalid_token","De verificatielink is ongeldig.");Validation.Identity(await users.ConfirmEmailAsync(user,Decode(request.Token)));return Results.NoContent();}).AllowAnonymous().RequireRateLimiting("auth");
        group.MapGet("/invitations/{token}",async(string token,AppDbContext db)=>{var hash=Hash(token);var invitation=await db.UserInvitations.AsNoTracking().SingleOrDefaultAsync(x=>x.TokenHash==hash)??throw InvalidInvitation();CheckInvitation(invitation);var organization=invitation.OrganizationId is Guid id?await db.Organizations.Where(x=>x.Id==id).Select(x=>x.Name).SingleAsync():null;return Results.Ok(new{invitation.Email,invitation.Role,organization,invitation.ExpiresAt});}).AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost("/accept-invitation",AcceptInvitation).AllowAnonymous().RequireRateLimiting("auth");
        app.MapGet("/api/invitations",async(AppDbContext db,CurrentActor actor,Guid? organizationId)=>{DemandAdmin(actor);return Results.Ok(await db.UserInvitations.AsNoTracking().Where(x=>!organizationId.HasValue||x.OrganizationId==organizationId).OrderByDescending(x=>x.CreatedAt).Select(x=>new{x.Id,x.Email,x.OrganizationId,x.Role,x.Permissions,x.CreatedAt,x.ExpiresAt,x.RevokedAt,x.AcceptedAt}).Take(200).ToListAsync());}).RequireAuthorization();
        app.MapPost("/api/invitations",async(InvitationRequest request,AppDbContext db,CurrentActor actor,UserManager<ApplicationUser> users,IEmailService email,IConfiguration config,AuditService audit)=>{
            DemandAdmin(actor);var address=Validation.Required(request.Email,"e-mailadres",254);if(!System.Net.Mail.MailAddress.TryCreate(address,out _))throw new ApiException(400,"validation","Het e-mailadres is ongeldig.");
            if(await users.FindByEmailAsync(address)!=null)throw new ApiException(409,"account_exists","Voor dit adres bestaat al een account.");
            var role=request.Role??"Customer";if(role is not("Customer" or "Employee"))throw new ApiException(400,"validation","Kies Customer of Employee.");
            if(role=="Customer"&&request.OrganizationId is null)throw new ApiException(400,"validation","Koppel de klant aan een organisatie.");
            if(request.OrganizationId is Guid organization&&!await db.Organizations.AnyAsync(x=>x.Id==organization&&!x.IsArchived))throw NotFound();
            var permissions=ValidatePermissions(request.Permissions??[],role);
            var token=WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var invitation=new UserInvitation{Email=address,OrganizationId=request.OrganizationId,Role=role,Permissions=permissions,TokenHash=Hash(token),CreatedBy=actor.UserId};
            db.UserInvitations.Add(invitation);await db.SaveChangesAsync();var delivery=await SendInvitation(invitation,token,email,config);await audit.RecordAsync("invitation.created","invitation",invitation.Id.ToString(),organizationId:invitation.OrganizationId);return Results.Ok(new{invitation.Id,invitation.Email,invitation.ExpiresAt,delivery});
        }).RequireAuthorization();
        app.MapPost("/api/invitations/{id:guid}/resend",async(Guid id,AppDbContext db,CurrentActor actor,IEmailService email,IConfiguration config,AuditService audit)=>{DemandAdmin(actor);var invitation=await db.UserInvitations.FindAsync(id)??throw NotFound();if(invitation.AcceptedAt!=null)throw new ApiException(409,"accepted","Deze uitnodiging is al gebruikt.");var token=WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));invitation.TokenHash=Hash(token);invitation.RevokedAt=null;invitation.ExpiresAt=DateTimeOffset.UtcNow.AddDays(7);await db.SaveChangesAsync();var delivery=await SendInvitation(invitation,token,email,config);await audit.RecordAsync("invitation.resent","invitation",id.ToString());return Results.Ok(new{invitation.Id,invitation.ExpiresAt,delivery});}).RequireAuthorization();
        app.MapDelete("/api/invitations/{id:guid}",async(Guid id,AppDbContext db,CurrentActor actor,AuditService audit)=>{DemandAdmin(actor);var invitation=await db.UserInvitations.FindAsync(id)??throw NotFound();invitation.RevokedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync();await audit.RecordAsync("invitation.revoked","invitation",id.ToString());return Results.NoContent();}).RequireAuthorization();
        app.MapGet("/api/users",async(CurrentActor actor,UserManager<ApplicationUser> users,Guid? organizationId)=>{DemandAdmin(actor);var all=await users.Users.AsNoTracking().Where(x=>!organizationId.HasValue||x.OrganizationId==organizationId).OrderBy(x=>x.Email).Take(200).ToListAsync();var output=new List<object>();foreach(var user in all)output.Add(new{user.Id,user.Email,name=user.DisplayName,user.OrganizationId,user.IsActive,user.TwoFactorEnabled,role=(await users.GetRolesAsync(user)).FirstOrDefault(),permissions=(await users.GetClaimsAsync(user)).Where(x=>x.Type=="permission").Select(x=>x.Value)});return Results.Ok(output);}).RequireAuthorization();
        app.MapPut("/api/users/{id}",UpdateUser).RequireAuthorization();
        app.MapPost("/api/users/{id}/revoke-sessions",async(string id,CurrentActor actor,UserManager<ApplicationUser> users,AuthService auth,AuditService audit)=>{DemandAdmin(actor);var user=await users.FindByIdAsync(id)??throw NotFound();await auth.RevokeAllAsync(user);await audit.RecordAsync("account.admin_revoked_sessions","user",id);return Results.NoContent();}).RequireAuthorization();
    }
    private static async Task<IResult> AcceptInvitation(AcceptRequest request,AppDbContext db,UserManager<ApplicationUser> users,AuditService audit)
    {
        var hash=Hash(request.Token);await using var transaction=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var invitation=await db.UserInvitations.SingleOrDefaultAsync(x=>x.TokenHash==hash)??throw InvalidInvitation();CheckInvitation(invitation);
        if(invitation.OrganizationId is Guid org&&!await db.Organizations.AnyAsync(x=>x.Id==org&&!x.IsArchived))throw InvalidInvitation();
        if(await users.FindByEmailAsync(invitation.Email)!=null)throw new ApiException(409,"account_exists","Voor dit e-mailadres bestaat al een account.");
        var user=new ApplicationUser{UserName=invitation.Email,Email=invitation.Email,EmailConfirmed=true,DisplayName=Validation.Required(request.Name,"je naam"),OrganizationId=invitation.OrganizationId};
        Validation.Identity(await users.CreateAsync(user,request.Password));Validation.Identity(await users.AddToRoleAsync(user,invitation.Role));
        if(invitation.Role=="Employee")foreach(var permission in invitation.Permissions)Validation.Identity(await users.AddClaimAsync(user,new("permission",permission)));
        invitation.AcceptedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync();await audit.RecordAsync("invitation.accepted","user",user.Id,organizationId:user.OrganizationId);await transaction.CommitAsync();return Results.Ok(new{message="Je account is aangemaakt en je e-mailadres is geverifieerd. Je kunt nu inloggen."});
    }
    private static async Task<IResult> UpdateUser(string id,UpdateUserRequest request,CurrentActor actor,AppDbContext db,UserManager<ApplicationUser> users,AuthService auth,AuditService audit)
    {
        DemandAdmin(actor);var user=await users.FindByIdAsync(id)??throw NotFound();var currentRoles=await users.GetRolesAsync(user);
        if(request.Role is not("SuperAdmin" or "Employee" or "Customer"))throw new ApiException(400,"validation","Ongeldige rol.");
        if(user.Id==actor.UserId&&(!request.IsActive||request.Role!="SuperAdmin"))throw new ApiException(400,"validation","Je kunt je eigen beheerdersaccount niet uitschakelen of degraderen.");
        if(request.Role=="Customer"&&request.OrganizationId==null)throw new ApiException(400,"validation","Koppel de klant aan een organisatie.");
        if(request.OrganizationId is Guid org&&!await db.Organizations.AnyAsync(x=>x.Id==org&&!x.IsArchived))throw NotFound();
        var permissions=ValidatePermissions(request.Permissions??[],request.Role);
        await using var transaction=await db.Database.BeginTransactionAsync();
        user.DisplayName=Validation.Required(request.Name,"de naam");user.OrganizationId=request.OrganizationId;user.IsActive=request.IsActive;
        Validation.Identity(await users.UpdateAsync(user));Validation.Identity(await users.RemoveFromRolesAsync(user,currentRoles));Validation.Identity(await users.AddToRoleAsync(user,request.Role));
        var claims=(await users.GetClaimsAsync(user)).Where(x=>x.Type=="permission").ToArray();Validation.Identity(await users.RemoveClaimsAsync(user,claims));
        foreach(var permission in permissions)Validation.Identity(await users.AddClaimAsync(user,new("permission",permission)));
        await auth.RevokeAllAsync(user);await audit.RecordAsync("account.updated","user",id);await transaction.CommitAsync();return Results.NoContent();
    }
    private static string[] ValidatePermissions(string[] permissions,string role)
    {if(role!="Employee")return [];if(permissions.Any(x=>!Infrastructure.Permissions.All.Contains(x)||x is "users.manage" or "settings.manage"))throw new ApiException(400,"validation","Ongeldige medewerkerrechten.");return permissions.Distinct().ToArray();}
    private static Task<EmailDelivery> SendInvitation(UserInvitation invitation,string token,IEmailService email,IConfiguration config)
    {var link=$"{PortalUrl(config)}/uitnodiging?token={Uri.EscapeDataString(token)}";return email.SendAsync(invitation.Email,"Je uitnodiging voor Coffa OS",$"<p>Je bent uitgenodigd voor Coffa OS.</p><p><a href=\"{HtmlEncoder.Default.Encode(link)}\">Activeer je account</a></p><p>Deze persoonlijke link verloopt na zeven dagen.</p>");}
    private static string PortalUrl(IConfiguration config)=>(config["Public:PortalUrl"]??"http://localhost:3000/portaal").TrimEnd('/');
    private static string NormalizeCode(string value)=>value.Replace(" ","").Replace("-","");
    private static string Hash(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token??"")));
    private static string Encode(string token)=>WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    private static string Decode(string token){try{return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));}catch{throw new ApiException(400,"invalid_token","De link is ongeldig of verlopen.");}}
    private static void CheckInvitation(UserInvitation invitation){if(invitation.ExpiresAt<=DateTimeOffset.UtcNow||invitation.RevokedAt!=null||invitation.AcceptedAt!=null)throw InvalidInvitation();}
    private static ApiException InvalidInvitation()=>new(400,"invalid_invitation","Deze uitnodiging is ongeldig, verlopen of al gebruikt.");
    private static ApiException InvalidLogin()=>new(401,"invalid_login","E-mail, wachtwoord of verificatiecode klopt niet.");
    private static ApiException NotFound()=>new(404,"not_found","Dit onderdeel is niet gevonden.");
    private static void DemandAdmin(CurrentActor actor){if(!actor.IsAdmin)throw new ApiException(403,"forbidden","Alleen de beheerder kan accounts beheren.");}
    private static async Task<ApplicationUser> CurrentUser(HttpContext context,UserManager<ApplicationUser> users)=>await users.GetUserAsync(context.User)??throw new ApiException(401,"unauthenticated","Log eerst in.");
}
