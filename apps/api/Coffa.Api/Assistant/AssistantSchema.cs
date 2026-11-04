namespace Coffa.Api.Assistant;

public static class AssistantSchema
{
    private static object Text(bool nullable = false) => nullable ? new { type = new[] { "string", "null" } } : (object)new { type = "string" };
    private static object Number() => new { type = new[] { "number", "null" } };
    private static object List(object item) => new { type = "array", items = item };
    private static object Shape(Dictionary<string, object> properties) => new { type = "object", properties, required = properties.Keys.ToArray(), additionalProperties = false };
    public static readonly string[] Kinds = ["create_organization", "create_contact", "create_lead", "create_note", "create_project", "create_task", "create_time_entry", "create_ticket", "create_quote", "create_invoice", "create_subscription"];
    public static object Value { get; } = Shape(new()
    {
        ["message"] = Text(),
        ["questions"] = List(Text()),
        ["actions"] = List(Shape(new()
        {
            ["kind"] = new { type = "string", @enum = Kinds },
            ["summary"] = Text(), ["organizationName"] = Text(true), ["projectName"] = Text(true),
            ["fields"] = List(Shape(new() { ["name"] = Text(), ["value"] = new { anyOf = new object[] { Text(), new { type = "number" }, new { type = "boolean" }, new { type = "null" }, List(Text()) } } })),
            ["lines"] = List(Shape(new() { ["description"] = Text(), ["quantity"] = Number(), ["unitPrice"] = Number(), ["discountPercent"] = Number(), ["taxRate"] = Number() })),
            ["warnings"] = List(Text())
        }))
    });
}
