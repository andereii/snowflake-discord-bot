using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Snowflake.Bot.Configuration;
using Snowflake.Bot.Services.AiCommands;

namespace Snowflake.Bot.Services.Ai;

/// <summary>
/// Backend de la Responses API de DeepSeek. La búsqueda web usa la tool
/// nativa "web_search" con tool_choice "auto" (el modelo decide cuándo buscar).
/// </summary>
public sealed class DeepSeekBackend : IAiBackend
{
    private const string Endpoint = "https://api.deepseek.com/responses";
    private const string WebSearchTool = "web_search";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly IOptionsMonitor<AiOptions> _options;

    public DeepSeekBackend(IHttpClientFactory httpFactory, IOptionsMonitor<AiOptions> options)
    {
        _httpFactory = httpFactory;
        _options = options;
    }

    public string Nombre => "DeepSeek";

    public bool Disponible =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY"));

    public async Task<RespuestaBackend> LlamarAsync(
        IReadOnlyList<ItemHistorial> historial,
        IReadOnlyList<ToolDef> tools,
        bool conBusqueda,
        CancellationToken ct)
    {
        var clave = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")?.Trim();
        if (string.IsNullOrEmpty(clave))
            throw new AiApiKeyMissingException("No AI API key is configured.");

        var opts = _options.CurrentValue;

        var todas = new List<ToolJson>();
        if (conBusqueda) todas.Add(new ToolJson(WebSearchTool));
        foreach (var t in tools)
            todas.Add(new ToolJson("function", t.Nombre, t.Descripcion, t.Esquema));

        var req = new RequestJson(
            ModeloActivo(opts),
            Instructions: opts.SystemPrompt,
            Input: ConvertirHistorial(historial),
            Tools: todas.Count > 0 ? todas : null,
            ToolChoice: todas.Count > 0 ? "auto" : null,
            Temperature: opts.Temperature,
            MaxOutputTokens: opts.MaxOutputTokens,
            Stream: false);

        using var mensaje = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(req, JsonOpts),
                MediaTypeHeaderValue.Parse("application/json"))
        };
        mensaje.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clave);

        var http = _httpFactory.CreateClient("DeepSeek");
        using var resp = await EnviarAsync(http, mensaje, ct).ConfigureAwait(false);
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            var detalle = json.RootElement.TryGetProperty("error", out var err)
                && err.TryGetProperty("message", out var msg)
                    ? msg.GetString() ?? $"HTTP {(int)resp.StatusCode}"
                    : $"HTTP {(int)resp.StatusCode}";
            throw new AiException($"The AI provider responded with an error: {detalle}");
        }

        return ExtraerRespuesta(json.RootElement);
    }

    private static string ModeloActivo(AiOptions opts)
    {
        var env = Environment.GetEnvironmentVariable("DEEPSEEK_MODEL")?.Trim();
        return !string.IsNullOrEmpty(env) ? env : opts.Model;
    }

    private static async Task<HttpResponseMessage> EnviarAsync(
        HttpClient http, HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AiException("Could not reach the AI provider: " + ex.Message, ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new AiException("The AI provider took too long to respond (timeout).", ex);
        }
    }

    /// <summary>Convierte el historial normalizado a items de la Responses API.</summary>
    private static List<JsonNode> ConvertirHistorial(IReadOnlyList<ItemHistorial> historial)
    {
        var input = new List<JsonNode>();
        var vistosCall = new HashSet<string>(StringComparer.Ordinal);
        var vistosOutput = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in historial)
        {
            switch (item)
            {
                case ItemMensaje m:
                    input.Add(JsonNode.Parse(JsonSerializer.Serialize(new
                    {
                        type = "message",
                        role = m.Rol,
                        content = m.Texto
                    }))!);
                    break;

                case ItemLlamadaFuncion f:
                    if (!vistosCall.Add(f.CallId)) continue;
                    input.Add(JsonNode.Parse(JsonSerializer.Serialize(new
                    {
                        type = "function_call",
                        call_id = f.CallId,
                        name = f.Nombre,
                        arguments = f.ArgsJson
                    }))!);
                    break;

                case ItemResultadoFuncion r:
                    if (!vistosOutput.Add(r.CallId)) continue;
                    input.Add(JsonNode.Parse(JsonSerializer.Serialize(new
                    {
                        type = "function_call_output",
                        call_id = r.CallId,
                        output = r.Salida
                    }))!);
                    break;

                case ItemBusquedaWeb:
                    input.Add(JsonNode.Parse("{\"type\":\"web_search_call\"}")!);
                    break;
            }
        }

        return input;
    }

    private static RespuestaBackend ExtraerRespuesta(JsonElement root)
    {
        var salida = new List<ItemHistorial>();
        var llamadas = new List<LlamadaFuncion>();
        var sb = new StringBuilder();
        var usoBusqueda = false;

        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                var tipo = item.TryGetProperty("type", out var tipoEl) ? tipoEl.GetString() : null;

                if (tipo == "function_call")
                {
                    var nombre = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var callId = item.TryGetProperty("call_id", out var c) ? c.GetString() ?? "" : "";
                    var args = item.TryGetProperty("arguments", out var a) ? a.GetString() ?? "" : "";
                    llamadas.Add(new LlamadaFuncion(callId, nombre, args));
                    salida.Add(new ItemLlamadaFuncion(callId, nombre, args));
                }
                else if (tipo == "web_search_call")
                {
                    usoBusqueda = true;
                    salida.Add(new ItemBusquedaWeb());
                }
                else if (tipo == "message" && item.TryGetProperty("content", out var content))
                {
                    var texto = new StringBuilder();
                    if (content.ValueKind == JsonValueKind.String)
                    {
                        texto.Append(content.GetString());
                    }
                    else if (content.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var parte in content.EnumerateArray())
                            if (parte.TryGetProperty("text", out var t))
                                texto.Append(t.GetString());
                    }

                    sb.Append(texto);
                    salida.Add(new ItemMensaje("assistant", texto.ToString()));
                }
            }
        }

        return new RespuestaBackend(sb.ToString(), llamadas, salida, usoBusqueda);
    }

    private sealed record ToolJson(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("name")] string? Name = null,
        [property: JsonPropertyName("description")] string? Description = null,
        [property: JsonPropertyName("parameters")] JsonNode? Parameters = null);

    private sealed record RequestJson(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("instructions")] string Instructions,
        [property: JsonPropertyName("input")] List<JsonNode> Input,
        [property: JsonPropertyName("tools")] List<ToolJson>? Tools,
        [property: JsonPropertyName("tool_choice")] string? ToolChoice,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_output_tokens")] int MaxOutputTokens,
        [property: JsonPropertyName("stream")] bool Stream);
}
