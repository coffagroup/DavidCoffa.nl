using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
namespace Coffa.Api.Infrastructure;

public record EmailAttachment(string FileName,byte[] Content,string ContentType);
public record EmailMessage(string Recipient,string Subject,string HtmlBody,IReadOnlyList<EmailAttachment>? Attachments=null);
public record EmailDelivery(bool Sent,bool DevelopmentOutbox,string? OutboxId);
public static class BrandedEmailTemplate
{
    public static string Render(string content) => $$"""
        <!doctype html>
        <html lang="nl">
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="light"><style>a{color:#45654c!important}p{margin:0 0 16px}p:last-child{margin-bottom:0}</style></head>
        <body style="margin:0;padding:32px 16px;background-color:#f7f6f1;color:#1b2821;font-family:Arial,Helvetica,sans-serif;">
          <div style="display:none;max-height:0;overflow:hidden;opacity:0;">Een bericht van David Coffa</div>
          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:620px;margin:0 auto;">
            <tr><td style="padding:0 0 18px;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0"><tr>
                <td style="font-size:28px;font-weight:700;letter-spacing:-2px;color:#1b2821;">dc<span style="color:#75967a;">.</span></td>
                <td align="right" style="font-size:11px;letter-spacing:1px;color:#69736b;">SOFTWARE · WEB · DATA · AUTOMATION</td>
              </tr></table>
            </td></tr>
            <tr><td style="padding:32px;background:#fffefa;border:1px solid #d9ded4;border-radius:10px;font-size:15px;line-height:1.75;">
              <div style="color:#344339;">{{content}}</div>
            </td></tr>
            <tr><td style="padding:20px 4px 0;color:#69736b;font-size:12px;line-height:1.6;">
              <strong style="color:#1b2821;">David Coffa</strong><br>
              Websites, software en AI-automatisering voor het mkb<br>
              <a href="https://davidcoffa.nl" style="color:#45654c;text-decoration:underline;">davidcoffa.nl</a>
            </td></tr>
          </table>
        </body>
        </html>
        """;
}
public interface IEmailService
{
    Task<EmailDelivery> SendAsync(string recipient,string subject,string htmlBody,CancellationToken cancellationToken=default);
    Task<EmailDelivery> SendAsync(EmailMessage message,CancellationToken cancellationToken=default);
}
public sealed class EmailService(IConfiguration config,IHostEnvironment environment) : IEmailService
{
    public Task<EmailDelivery> SendAsync(string recipient,string subject,string htmlBody,CancellationToken cancellationToken=default)=>SendAsync(new EmailMessage(recipient,subject,htmlBody),cancellationToken);
    public async Task<EmailDelivery> SendAsync(EmailMessage message,CancellationToken cancellationToken=default)
    {
        var brandedMessage=message with { HtmlBody=BrandedEmailTemplate.Render(message.HtmlBody) };
        if(!config.GetValue<bool>("Email:EnableSending"))
        {
            if(!environment.IsDevelopment()&&!environment.IsEnvironment("Testing"))throw new ApiException(503,"email_not_configured","E-mailverzending is nog niet geconfigureerd.");
            var directory=config["Email:OutboxPath"]??Path.Combine(environment.ContentRootPath,".private","outbox");
            Directory.CreateDirectory(directory);
            if(!OperatingSystem.IsWindows())File.SetUnixFileMode(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
            var id=Guid.NewGuid().ToString("N");var file=Path.Combine(directory,id+".json");
            await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{CreatedAt=DateTimeOffset.UtcNow,Message=brandedMessage}),cancellationToken);
            if(!OperatingSystem.IsWindows())File.SetUnixFileMode(file,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            return new(false,true,id);
        }
        var host=config["Email:SmtpHost"];
        if(string.IsNullOrWhiteSpace(host))throw new ApiException(503,"email_not_configured","De SMTP-configuratie ontbreekt.");
        var mail=new MimeMessage();mail.From.Add(new MailboxAddress(config["Email:FromName"]??"David Coffa",config["Email:From"]??"info@davidcoffa.nl"));mail.To.Add(MailboxAddress.Parse(message.Recipient));mail.Subject=message.Subject;
        var body=new BodyBuilder{HtmlBody=brandedMessage.HtmlBody};
        foreach(var attachment in brandedMessage.Attachments??[])body.Attachments.Add(attachment.FileName,attachment.Content,ContentType.Parse(attachment.ContentType));
        mail.Body=body.ToMessageBody();
        using var smtp=new SmtpClient();var port=config.GetValue("Email:SmtpPort",587);
        await smtp.ConnectAsync(host,port,port==465?SecureSocketOptions.SslOnConnect:SecureSocketOptions.StartTls,cancellationToken);
        if(!string.IsNullOrEmpty(config["Email:Username"]))await smtp.AuthenticateAsync(config["Email:Username"]!,config["Email:Password"]??"",cancellationToken);
        await smtp.SendAsync(mail,cancellationToken);await smtp.DisconnectAsync(true,cancellationToken);
        return new(true,false,null);
    }
}
