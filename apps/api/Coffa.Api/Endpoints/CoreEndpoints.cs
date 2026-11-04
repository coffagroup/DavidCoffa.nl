using Coffa.Api.Data;
using Coffa.Api.Domain;
using Coffa.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace Coffa.Api.Endpoints;

public static class CoreEndpoints
{
    public static void MapCore(this WebApplication app)
    {
        var group=app.MapGroup("/api").WithTags("CRM en instellingen").RequireAuthorization();
        group.MapGet("/activities",async(AppDbContext db,CurrentActor actor,Guid? organizationId,int? page,int? pageSize)=>{
            var query=db.AuditEntries.AsNoTracking().AsQueryable();
            if(organizationId.HasValue)query=query.Where(x=>x.OrganizationId==organizationId);
            if(actor.IsCustomer)query=query.Where(x=>x.UserId==actor.UserId&&x.OrganizationId==actor.OrganizationId);
            else if(!actor.IsAdmin){var crm=actor.HasPermission("crm.read");var projects=actor.HasPermission("projects.read");var tickets=actor.HasPermission("tickets.read");var documents=actor.HasPermission("documents.read");var finance=actor.HasPermission("finance.read");query=query.Where(x=>(crm&&(x.EntityType=="organization"||x.EntityType=="lead"))||(projects&&(x.EntityType=="project"||x.EntityType=="task"))||(tickets&&x.EntityType=="ticket")||(documents&&x.EntityType=="document")||(finance&&(x.EntityType=="invoice"||x.EntityType=="quote"||x.EntityType=="payment"||x.EntityType=="subscription"||x.EntityType=="expense")));}
            var paging=Validation.Page(page,pageSize);var total=await query.CountAsync();var items=await query.OrderByDescending(x=>x.CreatedAt).Skip((paging.Page-1)*paging.PageSize).Take(paging.PageSize).Select(x=>new{x.Id,x.Action,x.EntityType,x.EntityId,x.OrganizationId,x.CreatedAt,details=actor.IsCustomer?null:x.Details}).ToListAsync();return Results.Ok(new{items,total,page=paging.Page,pageSize=paging.PageSize});
        });
        group.MapGet("/settings",async(AppDbContext db,CurrentActor actor)=>{actor.DemandPermission("settings.manage");return Results.Ok(await db.BusinessSettings.FindAsync(1)??new BusinessSettings());});
        group.MapPut("/settings",async(BusinessSettings request,AppDbContext db,CurrentActor actor,AuditService audit)=>{
            actor.DemandPermission("settings.manage");var settings=await db.BusinessSettings.FindAsync(1);if(settings==null){settings=new BusinessSettings();db.BusinessSettings.Add(settings);}settings.CompanyName=Validation.Required(request.CompanyName,"bedrijfsnaam");settings.Email=Validation.Required(request.Email,"e-mail",254);if(!System.Net.Mail.MailAddress.TryCreate(settings.Email,out _))throw new ApiException(400,"validation","Vul een geldig e-mailadres in.");settings.Address=Optional(request.Address,1000);settings.ChamberOfCommerce=Optional(request.ChamberOfCommerce,80);settings.VatNumber=Optional(request.VatNumber,80);settings.Iban=Optional(request.Iban,80);settings.PaymentInstructions=Optional(request.PaymentInstructions,4000);settings.InvoicePrefix=Prefix(request.InvoicePrefix,"factuurprefix");settings.QuotePrefix=Prefix(request.QuotePrefix,"offerteprefix");if(request.PaymentTermDays<1||request.PaymentTermDays>365)throw new ApiException(400,"validation","Gebruik een betaaltermijn van 1 tot 365 dagen.");settings.PaymentTermDays=request.PaymentTermDays;settings.AutomaticEmailEnabled=request.AutomaticEmailEnabled;await db.SaveChangesAsync();await audit.RecordAsync("settings.updated","settings","1");return Results.Ok(settings);
        });
        group.MapGet("/permissions",(CurrentActor actor)=>{actor.DemandPermission("users.manage");return Results.Ok(Permissions.All);});
    }
    private static string Prefix(string? value,string field){value=Validation.Required(value,field,20);if(!System.Text.RegularExpressions.Regex.IsMatch(value,@"^[A-Za-z0-9][A-Za-z0-9_-]{0,19}$"))throw new ApiException(400,"validation","Een nummerprefix mag alleen letters, cijfers, streepjes en underscores bevatten.");return value;}
    private static string Optional(string? value,int limit){value=value?.Trim()??"";if(value.Length>limit)throw new ApiException(400,"validation",$"Gebruik maximaal {limit} tekens.");return value;}
}
