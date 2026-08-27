using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Snowflake.Bot.Configuration;
using Snowflake.Bot.Services.AiCommands;

namespace Snowflake.Bot.Services.Ai;

/// <summary>
/// Backend de la API de Gemini (generativelanguage v1beta). La búsqueda web
/// usa el grounding nativo de Google Search y los comandos del bot usan
/// function calling; el modelo decide cuándo invocar cada cosa (modo AUTO).
/// </summary>
public sealed class GeminiBackend : IAiBackend
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly IOptionsMonitor<AiOptions> _options;

    public GeminiBackend(IHttpClientFactory httpFactory, IOptionsMonitor<AiOptions> options)
    {
        _httpFactory = httpFactory;
        _options = options;
    }

    public string Nombre => "Gemini";

    public bool Disponible =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY"));

    public async Task<RespuestaBackend> LlamarAsync(
        IReadOnlyList<ItemHistorial> historial,
        IReadOnlyList<ToolDef> tools,
        bool conBusqueda,
        CancellationToken ct)
    {
        var clave = Environment.GetEnvironmentVariable("GEMINI_API_KEY")?.Trim();
        if (string.IsNullOrEmpty(clave))
            throw new AiApiKeyMissingException("No AI API key is configured.");

        var opts = _options.CurrentValue;
        var modelo = ModeloActivo(opts);
        var url = $"{BaseUrl}/{modelo}:generateContent";

        var herramientas = new List<JsonObject>();
        if (conBusqueda)
            herramientas.Add(new JsonObject { ["google_search"] = new JsonObject() });
        if (tools.Count > 0)
        {
            var decls = new JsonArray();
            foreach (var t in tools)
            {
                decls.Add(new JsonObject
                {
                    ["name"] = t.Nombre,
                    ["description"] = t.Descripcion,
                    ["parameters"] = t.Esquema.DeepClone()
                });
            }
            herramientas.Add(new JsonObject { ["function_declarations"] = decls });
        }

        var req = new JsonObject
        {
            ["system_instruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = opts.SystemPrompt })
            },
            ["contents"] = ConvertirHistorial(historial),
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = opts.Temperature,
                ["maxOutputTokens"] = opts.MaxOutputTokens
            }
        };
        if (herramientas.Count > 0)
        {
            var arr = new JsonArray();
            foreach (var h in herramientas) arr.Add(h.DeepClone());
            req["tools"] = arr;
            req["tool_config"] = new JsonObject
            {
                ["function_calling_config"] = new JsonObject { ["mode"] = "AUTO" }
            };
        }

        using var mensaje = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(req.ToJsonString(JsonOpts),
                System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json"))
        };
        mensaje.Headers.TryAddWithoutValidation("x-goog-api-key", clave);

        var http = _httpFactory.CreateClient("Gemini");
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
        var env = Environment.GetEnvironmentVariable("GEMINI_MODEL")?.Trim();
        return !string.IsNullOrEmpty(env) ? env : opts.GeminiModel;
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

    /// <summary>
    /// Convierte el historial normalizado a "contents" de Gemini, fusionando
    /// items consecutivos del mismo rol en un solo content.
    /// </summary>
    private static JsonArray ConvertirHistorial(IReadOnlyList<ItemHistorial> historial)
    {
        var contents = new JsonArray();
        string? rolActual = null;
        JsonArray? partesActuales = null;

        void Cerrar()
        {
            if (rolActual is not null && partesActuales is not null)
            {
                contents.Add(new JsonObject
                {
                    ["role"] = rolActual,
                    ["parts"] = partesActuales
                });
            }
            rolActual = null;
            partesActuales = null;
        }

        void Abrir(string rol)
        {
            Cerrar();
            rolActual = rol;
            partesActuales = new JsonArray();
        }

        foreach (var item in historial)
        {
            switch (item)
            {
                case ItemBusquedaWeb:
                    continue;

                case ItemMensaje m:
                    var rol = m.Rol == "user" ? "user" : "model";
                    if (rol != rolActual) Abrir(rol);
                    partesActuales!.Add(new JsonObject { ["text"] = m.Texto });
                    break;

                case ItemLlamadaFuncion f:
                    if ("model" != rolActual) Abrir("model");
                    JsonNode? args = null;
                    try { args = JsonNode.Parse(string.IsNullOrWhiteSpace(f.ArgsJson) ? "{}" : f.ArgsJson); }
                    catch (JsonException) { args = new JsonObject(); }
                    partesActuales!.Add(new JsonObject
                    {
                        ["functionCall"] = new JsonObject
                        {
                            ["name"] = f.Nombre,
                            ["args"] = args
                        }
                    });
                    break;

                case ItemResultadoFuncion r:
                    if ("tool" != rolActual) Abrir("tool");
                    partesActuales!.Add(new JsonObject
                    {
                        ["functionResponse"] = new JsonObject
                        {
                            ["name"] = r.Nombre,
                            ["response"] = new JsonObject { ["result"] = r.Salida }
                        }
                    });
                    break;
            }
        }

        Cerrar();
        return contents;
    }

    private static RespuestaBackend ExtraerRespuesta(JsonElement root)
    {
        var salida = new List<ItemHistorial>();
        var llamadas = new List<LlamadaFuncion>();
        var sb = new StringBuilder();
        var usoBusqueda = false;

        if (!root.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0)
        {
            throw new AiException("The AI provider returned an empty response.");
        }

        foreach (var cand in candidates.EnumerateArray())
        {
            if (cand.TryGetProperty("groundingMetadata", out _))
                usoBusqueda = true;
        }

        var content = candidates[0].GetProperty("content");
        if (content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var t))
                {
                    var texto = t.GetString() ?? "";
                    sb.Append(texto);
                    salida.Add(new ItemMensaje("assistant", texto));
                }
                else if (part.TryGetProperty("functionCall", out var fc))
                {
                    var nombre = fc.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var args = fc.TryGetProperty("args", out var a)
                        ? a.GetRawText()
                        : "{}";
                    var callId = Guid.NewGuid().ToString("N");
                    llamadas.Add(new LlamadaFuncion(callId, nombre, args));
                    salida.Add(new ItemLlamadaFuncion(callId, nombre, args));
                }
            }
        }

        if (usoBusqueda)
            salida.Insert(0, new ItemBusquedaWeb());

        return new RespuestaBackend(sb.ToString(), llamadas, salida, usoBusqueda);
    }
}
