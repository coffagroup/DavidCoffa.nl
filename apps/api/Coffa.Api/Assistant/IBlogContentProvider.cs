using System.Text.Json;

namespace Coffa.Api.Assistant;

public interface IBlogContentProvider
{
    bool Configured { get; }
    Task<JsonElement> GenerateJsonAsync(string instructions, string input, JsonElement schema, string schemaName, CancellationToken cancellationToken);
}
