using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Coffa.Api.Data;
using Coffa.Api.Assistant;
using Coffa.Api.Commercial;
using Coffa.Api.Domain;
using Coffa.Api.Endpoints;
using Coffa.Api.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder=WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=24*1024*1024);
builder.Logging.ClearProviders();builder.Logging.AddJsonConsole();
var rawConnection=builder.Configuration.GetConnectionString("Default")??builder.Configuration["DATABASE_URL"];
if(string.IsNullOrWhiteSpace(rawConnection)){if(!builder.Environment.IsDevelopment()&&!builder.Environment.IsEnvironment("Testing"))throw new InvalidOperationException("Configureer ConnectionStrings__Default of DATABASE_URL voor PostgreSQL.");rawConnection="Host=127.0.0.1;Port=55439;Database=coffa_os_dev;Username=coffa_os";}
var connection=DatabaseConnection.Normalize(rawConnection);
var protection=builder.Services.AddDataProtection().SetApplicationName("Coffa.OS");
var keyPath=builder.Configuration["DataProtection:Path"];
if(!string.IsNullOrEmpty(keyPath))
{
    Directory.CreateDirectory(keyPath);
    if(!OperatingSystem.IsWindows())File.SetUnixFileMode(keyPath,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
    protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
}
else protection.PersistKeysToDbContext<AppDbContext>();
var certificatePath=builder.Configuration["DataProtection:CertificatePath"];
if(!string.IsNullOrEmpty(certificatePath))protection.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(certificatePath,builder.Configuration["DataProtection:CertificatePassword"]));
builder.Services.AddDbContext<AppDbContext>(o=>o.UseNpgsql(connection));
builder.Services.AddIdentity<ApplicationUser,IdentityRole>(o=>{o.User.RequireUniqueEmail=true;o.SignIn.RequireConfirmedEmail=true;o.Password.RequiredLength=12;o.Password.RequireNonAlphanumeric=false;o.Lockout.MaxFailedAccessAttempts=5;o.Lockout.DefaultLockoutTimeSpan=TimeSpan.FromMinutes(15);})
    .AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
builder.Services.Configure<DataProtectionTokenProviderOptions>(o=>o.TokenLifespan=TimeSpan.FromHours(2));
builder.Services.ConfigureApplicationCookie(o=>{
    o.Cookie.Name="coffa.session";o.Cookie.HttpOnly=true;o.Cookie.SameSite=SameSiteMode.Strict;o.Cookie.SecurePolicy=builder.Environment.IsDevelopment()||builder.Environment.IsEnvironment("Testing")?CookieSecurePolicy.SameAsRequest:CookieSecurePolicy.Always;o.SlidingExpiration=false;o.ExpireTimeSpan=TimeSpan.FromHours(8);
    o.Events.OnRedirectToLogin=c=>{c.Response.StatusCode=401;return Task.CompletedTask;};o.Events.OnRedirectToAccessDenied=c=>{c.Response.StatusCode=403;return Task.CompletedTask;};
    o.Events.OnValidatePrincipal=async c=>{
        var db=c.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        if(!Guid.TryParse(c.Principal?.FindFirstValue("session_id"),out var sid)){c.RejectPrincipal();return;}
        var session=await db.UserSessions.AsNoTracking().Include(x=>x.User).SingleOrDefaultAsync(x=>x.Id==sid);
        if(session?.User is not {IsActive:true} user||session.RevokedAt!=null||session.ExpiresAt<=DateTimeOffset.UtcNow||session.Version!=user.SessionVersion||c.Principal!.FindFirstValue(c.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityOptions>>().Value.ClaimsIdentity.SecurityStampClaimType)!=user.SecurityStamp||(c.Principal!.IsInRole("SuperAdmin")&&!session.SetupOnly&&!user.TwoFactorEnabled)||user.Id!=c.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)){c.RejectPrincipal();await c.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);}
    };
});
builder.Services.AddAuthorization(o=>{
    o.DefaultPolicy=new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim("session_kind","full").Build();
    o.AddPolicy("SetupOrFull",p=>p.RequireAuthenticatedUser());
});
builder.Services.AddAntiforgery(o=>{o.HeaderName="X-CSRF-TOKEN";o.Cookie.Name="coffa.csrf";o.Cookie.HttpOnly=true;o.Cookie.SameSite=SameSiteMode.Strict;o.Cookie.SecurePolicy=builder.Environment.IsDevelopment()||builder.Environment.IsEnvironment("Testing")?CookieSecurePolicy.SameAsRequest:CookieSecurePolicy.Always;});
builder.Services.AddHttpContextAccessor();builder.Services.AddScoped<CurrentActor>();builder.Services.AddScoped<AuditService>();builder.Services.AddScoped<AuthService>();builder.Services.AddScoped<IEmailService,EmailService>();
builder.Services.AddOpenApi();builder.AddOperations();builder.AddCommercial();builder.AddAssistant();builder.AddWebsiteAnalytics();
builder.Services.AddRateLimiter(o=>{o.RejectionStatusCode=429;o.AddPolicy("assistant",c=>RateLimitPartition.GetFixedWindowLimiter(c.User.FindFirstValue(ClaimTypes.NameIdentifier)??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=10,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));o.AddPolicy("public-contact",c=>RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=100,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));o.AddPolicy("auth",c=>RateLimitPartition.GetFixedWindowLimiter((c.Connection.RemoteIpAddress?.ToString()??"unknown")+c.Request.Path,_=>new FixedWindowRateLimiterOptions{PermitLimit=builder.Environment.IsEnvironment("Testing")?200:20,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));});
var app=builder.Build();
// Render enforces HTTPS externally and terminates TLS before Kestrel. This
// explicit hosting setting avoids trusting arbitrary X-Forwarded-* headers.
// Enable it only behind a proxy that enforces HTTPS and hides the app port.
if(builder.Configuration.GetValue<bool>("Hosting:HttpsOnlyProxy"))
    app.Use((context,next)=>{context.Request.Scheme="https";return next();});
app.Use(async(context,next)=>{context.Response.Headers.CacheControl="no-cache, no-store";context.Response.Headers.Pragma="no-cache";context.Response.Headers.XContentTypeOptions="nosniff";try{await next();}catch(ApiException e){context.Response.StatusCode=e.StatusCode;await context.Response.WriteAsJsonAsync(new{code=e.Code,message=e.Message});}catch(BadHttpRequestException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new{code="invalid_request",message="Controleer de ingevulde gegevens."});}catch(Exception e){app.Logger.LogError(e,"Unhandled API error {TraceId}",context.TraceIdentifier);context.Response.StatusCode=500;await context.Response.WriteAsJsonAsync(new{code="server_error",message="Er ging iets mis. Probeer het opnieuw.",traceId=context.TraceIdentifier});}});
app.UseAuthentication();app.UseAuthorization();app.UseRateLimiter();
app.Use(async(context,next)=>{if(context.GetEndpoint()?.Metadata.GetMetadata<PublicServerEndpoint>() is null&&!HttpMethods.IsGet(context.Request.Method)&&!HttpMethods.IsHead(context.Request.Method)&&!HttpMethods.IsOptions(context.Request.Method)){try{await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);}catch(AntiforgeryValidationException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new{code="csrf",message="Vernieuw de beveiligingstoken en probeer opnieuw."});return;}}await next();});
app.MapGet("/health",async(AppDbContext db)=>await db.Database.CanConnectAsync()?Results.Ok(new{status="healthy"}):Results.Json(new{status="unhealthy"},statusCode:503)).AllowAnonymous();
if(app.Environment.IsDevelopment()||app.Environment.IsEnvironment("Testing"))app.MapOpenApi();
app.MapAuth();app.MapCore();app.MapCommercial();app.MapOperations();app.MapAssistant();app.MapBlog();app.MapWebsiteAnalytics();
if(args.Contains("--migrate")){using var scope=app.Services.CreateScope();await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();return;}
if(builder.Configuration.GetValue<bool>("Database:AutoMigrate")){using var scope=app.Services.CreateScope();await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();}
await Bootstrap.InitializeAsync(app.Services,builder.Configuration);
if(args.Contains("--bootstrap-only"))return;
app.Run();
public partial class Program;
