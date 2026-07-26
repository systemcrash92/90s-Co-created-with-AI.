using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Seto90;

/// <summary>
/// Transporte MCP puro: JSON-RPC por stdio. Toda la logica de autoria vive en CommandSession
/// (Core) y en ToolRegistry; este archivo solo parsea, despacha y serializa. Asi el editor
/// visual puede hablar con la misma CommandSession sin pasar por stdio.
/// </summary>
public sealed class McpServer
{
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
    readonly Stream input;
    readonly Stream output;
    readonly CommandSession session;

    public McpServer(string projectPath, Stream input, Stream output)
    {
        this.input = input;
        this.output = output;
        session = new CommandSession(projectPath);
    }

    public async Task RunAsync()
    {
        using var reader = new StreamReader(input, Encoding.UTF8);
        await using var writer = new StreamWriter(output, new UTF8Encoding(false)) { AutoFlush = true };
        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            await writer.WriteLineAsync(Handle(line));
        }
    }

    string Handle(string line)
    {
        try
        {
            var req = JsonNode.Parse(line)!.AsObject();
            var id = req["id"]?.DeepClone();
            var method = req["method"]?.GetValue<string>() ?? "";
            var result = method switch
            {
                // `instructions` es el saludo del servidor: el agente llega sabiendo que preguntar
                // y en que orden trabajar, sin depender de que alguien le haya pasado la guia.
                "initialize" => JsonSerializer.SerializeToNode(new { protocolVersion = "2024-11-05", serverInfo = new { name = "90s-engine", version = "0.1.0" }, capabilities = new { tools = new { } }, instructions = ToolRegistry.Instructions }, JsonOptions),
                "tools/list" => JsonSerializer.SerializeToNode(new { tools = ToolRegistry.List() }, JsonOptions),
                "tools/call" => CallTool(req["params"]?.AsObject()),
                _ => throw new InvalidOperationException($"Metodo MCP no soportado: {method}")
            };
            return Resp(id, result!);
        }
        catch (Exception ex)
        {
            return Err(null, "internal_error", ex.Message);
        }
    }

    JsonNode CallTool(JsonObject? pars)
    {
        var name = pars?["name"]?.GetValue<string>() ?? "";
        var args = pars?["arguments"]?.AsObject() ?? [];
        var payload = ToolRegistry.Call(name, args, session);
        // Bitacora de co-autoria: cada escritura exitosa de la IA queda anotada como [ia],
        // simetrica a las notas [vos] del editor (las lecturas no anotan nada).
        if (payload.Ok && ToolRegistry.WriteNote(name, args) is { } note) session.Note(UiStrings.LogAi, note);
        return JsonSerializer.SerializeToNode(new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(payload, JsonOptions) } }, isError = !payload.Ok }, JsonOptions)!;
    }

    static string Resp(JsonNode? id, JsonNode result) => JsonSerializer.Serialize(new JsonObject { { "jsonrpc", "2.0" }, { "id", id }, { "result", result } }, JsonOptions);
    static string Err(JsonNode? id, string code, string msg) => JsonSerializer.Serialize(new JsonObject { { "jsonrpc", "2.0" }, { "id", id }, { "error", new JsonObject { { "code", code }, { "message", msg } } } }, JsonOptions);
}
