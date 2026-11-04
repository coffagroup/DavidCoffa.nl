using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace Coffa.Api.Infrastructure;

public interface IObjectStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default);
    Task<Stream> OpenAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

/// <summary>Private object storage; local disks are permitted only in development/test.</summary>
public sealed class ConfiguredObjectStorage(IConfiguration configuration, IHostEnvironment environment) : IObjectStorage
{
    private readonly Lazy<IAmazonS3?> client = new(() => CreateClient(configuration));
    private readonly string? bucket = configuration["Storage:Bucket"];
    private bool UseLocal => (environment.IsDevelopment() || environment.IsEnvironment("Testing")) && string.IsNullOrWhiteSpace(bucket);
    private string LocalPath(string key)
    {
        ValidateKey(key);
        var directory = configuration["Storage:DevelopmentDirectory"] ?? Path.Combine(environment.ContentRootPath, ".private", "objects");
        return Path.Combine(directory, key.Replace('/', Path.DirectorySeparatorChar));
    }
    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 250 || key.StartsWith('/') || key.Contains("..") || key.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.')))
            throw new ApiException(400, "invalid_storage_key", "Ongeldige bestandsreferentie.");
    }
    private IAmazonS3 GetClient() => client.Value ?? throw new ApiException(503, "storage_unconfigured", "Privébestandsopslag is nog niet geconfigureerd.");
    private static IAmazonS3? CreateClient(IConfiguration config)
    {
        var bucket = config["Storage:Bucket"];
        var accessKey = config["Storage:AccessKey"];
        var secretKey = config["Storage:SecretKey"];
        if (string.IsNullOrWhiteSpace(bucket) || string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey)) return null;
        var region = config["Storage:Region"] ?? "eu-west-1";
        var options = new AmazonS3Config { AuthenticationRegion = region, ForcePathStyle = true };
        var endpoint = config["Storage:Endpoint"];
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                throw new ApiException(503, "storage_configuration", "De opslagverbinding moet HTTPS gebruiken.");
            options.ServiceURL = uri.ToString();
        }
        else options.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region);
        return new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), options);
    }
    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        ValidateKey(key);
        if (UseLocal)
        {
            var path = LocalPath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.GetDirectoryName(path)!, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            await content.CopyToAsync(file, ct);
            await file.FlushAsync(ct);
            return;
        }
        await GetClient().PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = key, InputStream = content, ContentType = contentType, AutoCloseStream = false }, ct);
    }
    public async Task<Stream> OpenAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        if (UseLocal)
        {
            var path = LocalPath(key);
            if (!File.Exists(path)) throw new ApiException(404, "document_missing", "Het bestand is niet beschikbaar.");
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        }
        try
        {
            using var response = await GetClient().GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = key }, ct);
            if (response.ContentLength is > 20971520) throw new ApiException(413, "stored_file_too_large", "Het opgeslagen bestand is te groot.");
            var memory = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memory, ct);
            memory.Position = 0;
            return memory;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        { throw new ApiException(404, "document_missing", "Het bestand is niet beschikbaar."); }
    }
    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        ValidateKey(key);
        if (UseLocal) { File.Delete(LocalPath(key)); return; }
        await GetClient().DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucket, Key = key }, ct);
    }
}
