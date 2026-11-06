using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Coffa.Api.Commercial;
using Coffa.Api.Data;
using Coffa.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coffa.Api.Tests;

[Collection("api")]
public class CommercialTests(ApiFixture fixture)
{
    [Fact]
    public async Task FinancialMonthTotalsAndChartExcludeNextTwentiethAndIncludePaymentsOnOlderInvoices()
    {
        using var admin = await fixture.AdminAsync();
        var organization = await Organization(admin, "Financial boundaries");
        var start = new DateOnly(2098, 12, 20); var end = start.AddMonths(1);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var (day, amount) in new[] { (start.AddDays(-1), 1000m), (start, 10m), (end.AddDays(-1), 20m), (end, 2000m) })
            {
                var invoice = new Invoice { OrganizationId = organization, Status = "issued", InvoiceDate = day, DueDate = day, NetAmount = amount, TotalAmount = amount };
                invoice.Payments.Add(new InvoicePayment { InvoiceId = invoice.Id, IdempotencyKey = Guid.NewGuid(), Date = day, Amount = amount });
                db.Invoices.Add(invoice);
                db.Expenses.Add(new BusinessExpense { Date = day, NetAmount = amount });
            }
            var older = new Invoice { OrganizationId = organization, Status = "issued", InvoiceDate = start.AddMonths(-2), DueDate = start, NetAmount = 5m, TotalAmount = 5m };
            older.Payments.Add(new InvoicePayment { InvoiceId = older.Id, IdempotencyKey = Guid.NewGuid(), Date = start, Amount = 5m });
            db.Invoices.Add(older); await db.SaveChangesAsync();
        }
        var report = await Json(await admin.GetAsync($"/api/finance?from={start:yyyy-MM-dd}&to={end.AddDays(-1):yyyy-MM-dd}&financialMonth=true"));
        Assert.Equal(30m, report.GetProperty("invoicedRevenue").GetDecimal());
        Assert.Equal(35m, report.GetProperty("receivedPayments").GetDecimal());
        Assert.Equal(30m, report.GetProperty("expenses").GetDecimal());
        var bucket = Assert.Single(report.GetProperty("revenueSeries").EnumerateArray());
        Assert.Equal(30m, bucket.GetProperty("invoiced").GetDecimal());
        Assert.Equal(35m, bucket.GetProperty("received").GetDecimal());
        Assert.Equal(30m, bucket.GetProperty("costs").GetDecimal());
        Assert.Equal("2098-11-20", report.GetProperty("previous").GetProperty("from").GetString());
        var manual = await Json(await admin.GetAsync($"/api/finance?from={end:yyyy-MM-dd}&to={end:yyyy-MM-dd}"));
        Assert.Equal(2000m, manual.GetProperty("invoicedRevenue").GetDecimal());
    }

    [Fact]
    public void BrandedEmailTemplateWrapsTheMessageWithCoffaStyling()
    {
        var html = Coffa.Api.Infrastructure.BrandedEmailTemplate.Render("<p>Test message</p>");

        Assert.Contains("davidcoffa.nl", html);
        Assert.Contains("lang=\"nl\"", html);
        Assert.Contains("<p>Test message</p>", html);
    }

    [Fact]
    public void DecimalCalculationsApplyDiscountAndVatPerLineAndValidateInputs()
    {
        var result = CommercialCalculations.Calculate([new("Development", 2.5m, 85m, 10m, 21m), new("Hosting", 1, 9.99m, 0, 9m)]);
        Assert.Equal(201.24m, result.NetAmount); Assert.Equal(41.06m, result.TaxAmount); Assert.Equal(242.30m, result.TotalAmount);
        Assert.Equal(191.25m, result.Lines[0].NetAmount); Assert.Equal(40.16m, result.Lines[0].TaxAmount);
        Assert.Throws<Coffa.Api.Infrastructure.ApiException>(() => CommercialCalculations.Calculate([new("Invalid", -1, 10)]));
        Assert.Throws<Coffa.Api.Infrastructure.ApiException>(() => CommercialCalculations.Calculate([new("Invalid", 1, 10, 101)]));
        Assert.Equal(new DateOnly(2027,2,28), CommercialCalculations.NextBillingDate(new DateOnly(2027,1,31), "monthly", 31));
        Assert.Equal(new DateOnly(2027,3,31), CommercialCalculations.NextBillingDate(new DateOnly(2027,2,28), "monthly", 31));
        Assert.Equal(10m, CommercialCalculations.MonthlyAmount(new ServiceSubscription { Amount = 120, Frequency = "yearly" }));
    }

    [Fact]
    public async Task IssuedInvoicesAreImmutablePaymentsAreIdempotentAndCreditsPreserveOriginal()
    {
        using var admin = await fixture.AdminAsync(); await ConfigureCompany(admin); var organization = await Organization(admin, "Invoice client");
        var draft = await Json(await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/invoices", InvoiceInput(organization, "Original invoice"))); var id = draft.GetProperty("id").GetGuid();
        Assert.Equal("draft", draft.GetProperty("status").GetString()); Assert.Equal(121m, draft.GetProperty("totalAmount").GetDecimal());
        var issued = await Json(await ApiFixture.SendAsync(admin,HttpMethod.Post,$"/api/invoices/{id}/issue")); var number = issued.GetProperty("number").GetString(); Assert.StartsWith("TEST-", number);
        Assert.Equal(HttpStatusCode.Conflict, (await ApiFixture.SendAsync(admin,HttpMethod.Put,$"/api/invoices/{id}",InvoiceInput(organization,"Tampered"))).StatusCode);
        var key = Guid.NewGuid(); var payment = new { amount=50m,date=DateOnly.FromDateTime(DateTime.UtcNow),reference="Bank transfer",idempotencyKey=key };
        await Json(await ApiFixture.SendAsync(admin,HttpMethod.Post,$"/api/invoices/{id}/payments",payment)); await Json(await ApiFixture.SendAsync(admin,HttpMethod.Post,$"/api/invoices/{id}/payments",payment));
        var current = await admin.GetFromJsonAsync<JsonElement>($"/api/invoices/{id}"); Assert.Equal(50m,current.GetProperty("paidAmount").GetDecimal());Assert.Equal(71m,current.GetProperty("outstandingAmount").GetDecimal());Assert.Single(current.GetProperty("payments").EnumerateArray());
        var customerSummary = await admin.GetFromJsonAsync<JsonElement>($"/api/organizations/{organization}/commercial-summary");
        Assert.Equal(100m, customerSummary.GetProperty("invoicedRevenue").GetDecimal());
        Assert.Equal(50m, customerSummary.GetProperty("receivedPayments").GetDecimal());
        Assert.Equal(71m, customerSummary.GetProperty("outstandingAmount").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest,(await ApiFixture.SendAsync(admin,HttpMethod.Post,$"/api/invoices/{id}/payments",new{amount=72m,date=DateOnly.FromDateTime(DateTime.UtcNow),reference="Too much",idempotencyKey=Guid.NewGuid()})).StatusCode);
        var credit = await Json(await ApiFixture.SendAsync(admin,HttpMethod.Post,$"/api/invoices/{id}/credit",new{reason="Cancellation"})); Assert.Equal(-121m,credit.GetProperty("totalAmount").GetDecimal()); Assert.NotEqual(number, credit.GetProperty("number").GetString());
        var repeated = await Json(await ApiFixture.SendAsync(admin,HttpMethod.Post,$"/api/invoices/{id}/credit",new{reason="Retry"})); Assert.Equal(credit.GetProperty("id").GetGuid(),repeated.GetProperty("id").GetGuid());
        current = await admin.GetFromJsonAsync<JsonElement>($"/api/invoices/{id}");Assert.Equal(number,current.GetProperty("number").GetString());Assert.Equal(121m,current.GetProperty("totalAmount").GetDecimal());Assert.Equal(0m,current.GetProperty("outstandingAmount").GetDecimal());Assert.Equal(50m,current.GetProperty("creditBalance").GetDecimal());
    }

    [Fact]
    public async Task QuoteAcceptanceConversionsPdfAndCustomerIsolationWork()
    {
        using var admin = await fixture.AdminAsync(); await ConfigureCompany(admin); var organization = await Organization(admin,"Quotation client"); var other = await Organization(admin,"Other client");
        var request = new { organizationId=organization,projectId=(Guid?)null,title="Customer portal",validUntil=DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),notes="Shared scope",terms="Payment by agreement",sharedWithCustomer=true,lines=new[]{new{description="Portal development",quantity=2m,unitPrice=100m,discountPercent=0m,taxRate=21m}} };
        var quote = await Json(await ApiFixture.SendAsync(admin,HttpMethod.Post,"/api/quotes",request));var id=quote.GetProperty("id").GetGuid();
        await Json(await ApiFixture.SendAsync(admin,HttpMethod.Post,$"/api/quotes/{id}/issue"));
        using var customer = await Customer(organization); using var outsider = await Customer(other);
        Assert.Equal(HttpStatusCode.NotFound,(await outsider.GetAsync($"/api/quotes/{id}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await outsider.GetAsync($"/api/quotes/{id}/pdf")).StatusCode);
        var quoteDocuments = await Json(await customer.GetAsync($"/api/documents?quoteId={id}")); var quotePdfDocument = Assert.Single(quoteDocuments.GetProperty("items").EnumerateArray());
        Assert.Equal("Offerte PDF", quotePdfDocument.GetProperty("category").GetString());
        var storedQuotePdf = await customer.GetByteArrayAsync($"/api/documents/{quotePdfDocument.GetProperty("id").GetGuid()}/download");
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(storedQuotePdf, 0, 5));
        Assert.Equal(HttpStatusCode.Forbidden,(await customer.GetAsync("/api/finance")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await ApiFixture.SendAsync(customer,HttpMethod.Post,"/api/invoices",InvoiceInput(organization,"Unauthorized"))).StatusCode);
        await Json(await ApiFixture.SendAsync(customer,HttpMethod.Post,$"/api/quotes/{id}/accept"));
        var invoice=await Json(await ApiFixture.SendAsync(admin,HttpMethod.Post,$"/api/quotes/{id}/convert-to-invoice"));var retry=await Json(await ApiFixture.SendAsync(admin,HttpMethod.Post,$"/api/quotes/{id}/convert-to-invoice"));Assert.Equal(invoice.GetProperty("id").GetGuid(),retry.GetProperty("id").GetGuid());
        var project=await Json(await ApiFixture.SendAsync(admin,HttpMethod.Post,$"/api/quotes/{id}/convert-to-project"));Assert.Equal(organization,project.GetProperty("organizationId").GetGuid());
        var pdf=await customer.GetByteArrayAsync($"/api/quotes/{id}/pdf");Assert.True(pdf.Length>5000);Assert.Equal("%PDF-",Encoding.ASCII.GetString(pdf,0,5));
        var preview=Environment.GetEnvironmentVariable("COFFA_PDF_PREVIEW_PATH");if(!string.IsNullOrWhiteSpace(preview))await File.WriteAllBytesAsync(preview,pdf);
        Assert.Equal(HttpStatusCode.NotFound,(await customer.GetAsync($"/api/invoices/{invoice.GetProperty("id").GetGuid()}")).StatusCode);
    }

    [Fact]
    public async Task CurrentMonthDashboardShowsRevenueAndCostsAndInvoiceEmailStoresCustomerPdf()
    {
        using var admin = await fixture.AdminAsync(); await ConfigureCompany(admin); var organization = await Organization(admin, "Monthly dashboard client");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, FinancialMonth.Zone).DateTime); var monthStart = FinancialMonth.ForDate(today).Start;
        var baselineDashboard = await Json(await admin.GetAsync("/api/dashboard")); var baselineRevenue = baselineDashboard.GetProperty("kpis").GetProperty("monthlyRevenue").GetDecimal(); var baselineCosts = baselineDashboard.GetProperty("kpis").GetProperty("monthlyCosts").GetDecimal();
        var invoiceInput = new { organizationId = organization, projectId = (Guid?)null, title = "Monthly service", invoiceDate = today, dueDate = today.AddDays(14), notes = "", terms = "", sharedWithCustomer = true, lines = new[] { new { description = "Development", quantity = 1m, unitPrice = 100m, discountPercent = 0m, taxRate = 21m } } };
        var invoice = await Json(await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/invoices", invoiceInput)); var invoiceId = invoice.GetProperty("id").GetGuid();
        var delivery = await Json(await ApiFixture.SendAsync(admin, HttpMethod.Post, $"/api/invoices/{invoiceId}/send"));
        Assert.False(delivery.GetProperty("sent").GetBoolean()); Assert.True(delivery.GetProperty("developmentOutbox").GetBoolean());
        using var mailRecord = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.OutboxPath, delivery.GetProperty("outboxId").GetString() + ".json")));
        Assert.Contains($"factuur-TEST-{today.Year}-", mailRecord.RootElement.GetRawText());
        var issued = await Json(await admin.GetAsync($"/api/invoices/{invoiceId}")); Assert.StartsWith("TEST-", issued.GetProperty("number").GetString());
        var documents = await Json(await admin.GetAsync($"/api/documents?invoiceId={invoiceId}"));
        var generatedPdf = Assert.Single(documents.GetProperty("items").EnumerateArray());
        Assert.Equal("Factuur PDF", generatedPdf.GetProperty("category").GetString()); Assert.True(generatedPdf.GetProperty("sharedWithCustomer").GetBoolean());
        using var customer = await Customer(organization);
        var customerDocuments = await Json(await customer.GetAsync($"/api/documents?invoiceId={invoiceId}"));
        var customerPdf = Assert.Single(customerDocuments.GetProperty("items").EnumerateArray());
        var pdf = await customer.GetByteArrayAsync($"/api/documents/{customerPdf.GetProperty("id").GetGuid()}/download");
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));

        var expense = await Json(await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/expenses", new { date = today, supplier = "Hosting supplier", description = "Cloud hosting", netAmount = 40.12m, taxRate = 21m }));
        Assert.Equal(8.43m, expense.GetProperty("taxAmount").GetDecimal()); Assert.Equal(48.55m, expense.GetProperty("totalAmount").GetDecimal());
        await Json(await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/expenses", new { date = today.AddMonths(-1), supplier = "Earlier supplier", description = "Earlier cost", netAmount = 900m, taxRate = 21m }));
        var dashboard = await Json(await admin.GetAsync("/api/dashboard")); var kpis = dashboard.GetProperty("kpis");
        Assert.Equal(baselineRevenue + 100m, kpis.GetProperty("monthlyRevenue").GetDecimal()); Assert.Equal(baselineCosts + 40.12m, kpis.GetProperty("monthlyCosts").GetDecimal());
        Assert.False(kpis.TryGetProperty("outstandingInvoices", out _)); Assert.False(kpis.TryGetProperty("mrr", out _)); Assert.False(dashboard.TryGetProperty("revenueSeries", out _));
        var report = await Json(await admin.GetAsync($"/api/finance?from={monthStart:yyyy-MM-dd}&to={today:yyyy-MM-dd}"));
        Assert.Equal(baselineCosts + 40.12m, report.GetProperty("expenses").GetDecimal());
        var history = await Json(await admin.GetAsync($"/api/activities?organizationId={organization}"));
        Assert.Contains("invoice.pdf_generated", history.GetRawText()); Assert.Contains("invoice.email_development_outbox", history.GetRawText());

        var update = await Json(await ApiFixture.SendAsync(admin, HttpMethod.Put, $"/api/expenses/{expense.GetProperty("id").GetGuid()}", new { date = today, supplier = "Hosting supplier", description = "Cloud hosting", netAmount = 50m, taxRate = 21m }));
        Assert.Equal(50m, update.GetProperty("netAmount").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/expenses", new { date = today, supplier = "", description = "Invalid", netAmount = 1m, taxRate = 21m })).StatusCode);
    }

    [Fact]
    public async Task RecurringInvoicesAndTimeConversionCannotDuplicateAndReportsUseRealRecords()
    {
        using var admin=await fixture.AdminAsync();await ConfigureCompany(admin);var organization=await Organization(admin,"Recurring client");var today=DateOnly.FromDateTime(DateTime.UtcNow);
        var input=new{organizationId=organization,name="Hosting",description="Managed hosting",amount=20m,taxRate=21m,frequency="monthly",startDate=today,endDate=(DateOnly?)null,nextInvoiceDate=today,status="active",automaticInvoicing=true};
        var subscription=await Json(await ApiFixture.SendAsync(admin,HttpMethod.Post,"/api/subscriptions",input));var subscriptionId=subscription.GetProperty("id").GetGuid();
        await Json(await ApiFixture.SendAsync(admin,HttpMethod.Post,"/api/subscriptions/generate"));await Json(await ApiFixture.SendAsync(admin,HttpMethod.Post,"/api/subscriptions/generate"));
        using(var scope=fixture.Factory.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var invoices=await db.Invoices.Where(x=>x.SubscriptionId==subscriptionId).ToListAsync();Assert.Single(invoices);Assert.Equal("issued",invoices[0].Status);Assert.NotNull(invoices[0].Number);}
        Guid entryId;using(var scope=fixture.Factory.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var entry=new TimeEntry{OrganizationId=organization,UserId="historical-user",Description="Snapshot rate",WorkDate=today,DurationMinutes=90,HourlyRate=80m,IsBillable=true};db.TimeEntries.Add(entry);await db.SaveChangesAsync();entryId=entry.Id;}
        var timeInput=new{organizationId=organization,timeEntryIds=new[]{entryId},title="Worked hours"};var invoice=await Json(await ApiFixture.SendAsync(admin,HttpMethod.Post,"/api/invoices/from-time",timeInput));Assert.Equal(120m,invoice.GetProperty("netAmount").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict,(await ApiFixture.SendAsync(admin,HttpMethod.Post,"/api/invoices/from-time",timeInput)).StatusCode);
        var financial=await admin.GetFromJsonAsync<JsonElement>($"/api/finance?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");Assert.True(financial.GetProperty("invoicedRevenue").GetDecimal()>=20m);Assert.True(financial.GetProperty("revenueSeries").GetArrayLength()>0);
        var csv=await admin.GetStringAsync($"/api/finance/export?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");Assert.Contains("Factuurnummer",csv);
    }

    [Fact]
    public async Task HistoricalImportPersistsPdfPaymentsAndRejectsDuplicateNumber()
    {
        using var admin=await fixture.AdminAsync();await ConfigureCompany(admin);var organization=await Organization(admin,"Historical client");var number="OLD-"+Guid.NewGuid().ToString("N");var today=DateOnly.FromDateTime(DateTime.UtcNow);
        var metadata=new{organizationId=organization,number,invoiceDate=today.AddDays(-30),dueDate=today.AddDays(-16),netAmount=100m,taxAmount=21m,totalAmount=121m,paidAmount=30m,paymentDate=today.AddDays(-20)};
        async Task<HttpResponseMessage> Import()
        {
            var csrf=await admin.GetFromJsonAsync<JsonElement>("/api/auth/csrf");using var request=new HttpRequestMessage(HttpMethod.Post,"/api/invoices/import");request.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());
            var form=new MultipartFormDataContent();form.Add(new StringContent(JsonSerializer.Serialize(metadata)),"metadata");var file=new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF"));file.Headers.ContentType=new("application/pdf");form.Add(file,"file","historical.pdf");request.Content=form;return await admin.SendAsync(request);
        }
        var imported=await Json(await Import());Assert.Equal("historical",imported.GetProperty("kind").GetString());Assert.Equal(91m,imported.GetProperty("outstandingAmount").GetDecimal());Assert.Equal(HttpStatusCode.Conflict,(await Import()).StatusCode);
        var id=imported.GetProperty("id").GetGuid();Assert.Equal("%PDF-1.4\n%%EOF",await admin.GetStringAsync($"/api/invoices/{id}/pdf"));
    }

    private static Task<JsonElement> Json(HttpResponseMessage response)=>ApiFixture.JsonAsync(response);
    private static object InvoiceInput(Guid organization,string title)=>new{organizationId=organization,projectId=(Guid?)null,title,invoiceDate=DateOnly.FromDateTime(DateTime.UtcNow),dueDate=DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14),notes="",terms="",sharedWithCustomer=false,lines=new[]{new{description="Development",quantity=1m,unitPrice=100m,discountPercent=0m,taxRate=21m}}};
    private static async Task ConfigureCompany(HttpClient admin)=>await Json(await ApiFixture.SendAsync(admin,HttpMethod.Put,"/api/settings",new{companyName="Coffa test company",email="test@example.com",address="Teststraat 1, 1234 AB Voorburg",chamberOfCommerce="12345678",vatNumber="NL000000000B01",iban="NL00TEST0000000000",paymentInstructions="Test payment instructions",invoicePrefix="TEST",quotePrefix="OFFTEST",paymentTermDays=14,automaticEmailEnabled=false}));
    private static async Task<Guid> Organization(HttpClient admin,string name){var result=await Json(await ApiFixture.SendAsync(admin,HttpMethod.Post,"/api/organizations",new{name,email="test@example.com",phone="",website="",address="Example street 1",chamberOfCommerce="",vatNumber="",type="customer",status="active",tags=Array.Empty<string>(),internalNotes="Internal only"}));return result.GetProperty("id").GetGuid();}
    private async Task<HttpClient> Customer(Guid organization)
    {
        var email=$"finance-client-{Guid.NewGuid():N}@example.com";const string password="Customer-Password-42!";
        using(var scope=fixture.Factory.Services.CreateScope()){var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var user=new ApplicationUser{UserName=email,Email=email,EmailConfirmed=true,DisplayName="Test customer",OrganizationId=organization};Assert.True((await users.CreateAsync(user,password)).Succeeded);Assert.True((await users.AddToRoleAsync(user,"Customer")).Succeeded);}
        var client=fixture.CreateClient();await Json(await ApiFixture.SendAsync(client,HttpMethod.Post,"/api/auth/login",new{email,password}));return client;
    }
}
