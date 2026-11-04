using System.Security.Claims;
using Coffa.Api.Data;
using Coffa.Api.Domain;
using Microsoft.EntityFrameworkCore;
namespace Coffa.Api.Infrastructure;

public sealed class PublicServerEndpoint : Attribute;

public class ApiException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
public static class Permissions
{
    public static readonly string[] All = ["crm.read","crm.write","finance.read","finance.write","projects.read","projects.write","tickets.read","tickets.write","documents.read","documents.write","time.read","time.write","users.manage","settings.manage"];
}
public sealed class CurrentActor(IHttpContextAccessor accessor)
{
    private ClaimsPrincipal User => accessor.HttpContext?.User ?? new();
    public string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new ApiException(401,"unauthenticated","Log eerst in.");
    public Guid? OrganizationId => Guid.TryParse(User.FindFirstValue("organization_id"),out var id)?id:null;
    public string Role => User.FindFirstValue(ClaimTypes.Role) ?? "";
    public bool IsAdmin => User.IsInRole("SuperAdmin");
    public bool IsCustomer => User.IsInRole("Customer");
    public bool HasPermission(string permission) => IsAdmin || (!IsCustomer && User.HasClaim("permission",permission));
    public void DemandPermission(string permission) { if(!HasPermission(permission)) throw new ApiException(403,"forbidden","Je hebt geen toegang tot deze functie."); }
    public void DemandOrganization(Guid organizationId) { if(IsCustomer && OrganizationId!=organizationId) throw new ApiException(404,"not_found","Dit onderdeel is niet gevonden."); }
}
public sealed class AuditService(AppDbContext db,IHttpContextAccessor accessor)
{
    public async Task RecordAsync(string action,string entityType,string? entityId=null,string? details=null,Guid? organizationId=null,CancellationToken cancellationToken=default)
    {
        db.AuditEntries.Add(new AuditEntry {UserId=accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),Action=action,EntityType=entityType,EntityId=entityId,Details=details,OrganizationId=organizationId});
        await db.SaveChangesAsync(cancellationToken);
    }
}
public static class Validation
{
    public static string Required(string? value,string field,int max=200)
    { var result=value?.Trim()??""; if(result.Length==0||result.Length>max) throw new ApiException(400,"validation",$"Vul {field} in (maximaal {max} tekens)."); return result; }
    public static void Identity(Microsoft.AspNetCore.Identity.IdentityResult result)
    {if(!result.Succeeded)throw new ApiException(400,"validation",string.Join(" ",result.Errors.Select(x=>x.Description)));}
    public static (int Page,int PageSize) Page(int? page,int? pageSize)=>(Math.Max(1,page??1),Math.Clamp(pageSize??25,1,100));
}
