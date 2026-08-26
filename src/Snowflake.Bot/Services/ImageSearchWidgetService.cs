using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace Snowflake.Bot.Services;

public sealed class ImageSearchWidgetService(IHttpClientFactory _httpFactory, MessagesService _msg, ILogger<ImageSearchWidgetService> _logger)
{
    public const string BtnPrev = "img_prev";
    public const string BtnNext = "img_next";
    public const string BtnDel = "img_del";
    
    private static readonly string[] BotonesIds = [BtnPrev, BtnNext, BtnDel];
    public static bool EsInteraccion(string id) => BotonesIds.Contains(id);

    private sealed record Session(ulong UserId, string Query, List<string> Urls)
    {
        public int Index { get; set; } = 0;
    }

    private readonly ConcurrentDictionary<ulong, Session> _sessions = new(); // MessageId -> Session

    public async Task<List<string>> BuscarAsync(string query)
    {
        try
        {
            var http = _httpFactory.CreateClient("DuckDuckGo");

            http.DefaultRequestHeaders.Clear();
            http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

            var res1 = await http.GetStringAsync($"https://duckduckgo.com/?q={Uri.EscapeDataString(query)}&t=h_&iax=images&ia=images");
            var vqdMatch = Regex.Match(res1, @"vqd=""([^""]+)""");
            if (!vqdMatch.Success) vqdMatch = Regex.Match(res1, @"vqd=([\d-]+)");
            if (!vqdMatch.Success) return [];

            var vqd = vqdMatch.Groups[1].Value;
            var res2 = await http.GetStringAsync($"https://duckduckgo.com/i.js?l=us-en&o=json&q={Uri.EscapeDataString(query)}&vqd={vqd}&f=,,,,,&p=1");
            
            var doc = JsonDocument.Parse(res2);
            var results = doc.RootElement.GetProperty("results");
            var urls = new List<string>();
            foreach(var item in results.EnumerateArray())
            {
                if (item.TryGetProperty("image", out var img))
                    urls.Add(img.GetString()!);
            }
            return urls;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error buscando imágenes para '{Query}'", query);
            return [];
        }
    }

    public void Registrar(ulong messageId, ulong userId, string query, List<string> urls)
    {
        _sessions[messageId] = new Session(userId, query, urls);
    }

    public async Task ManejarBotonAsync(ComponentInteractionCreateEventArgs e)
    {
        if (!_sessions.TryGetValue(e.Message.Id, out var session))
        {
            await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            return;
        }

        if (e.User.Id != session.UserId && e.Id != BtnDel)
        {
            await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent(_msg.Get(e.Guild!.Id, "Errores:NoEresAutor")).AsEphemeral());
            return;
        }

        if (e.Id == BtnDel)
        {
            if (e.User.Id != session.UserId)
            {
                var guildUser = await e.Guild!.GetMemberAsync(e.User.Id);
                if (!guildUser.Permissions.HasPermission(Permissions.ManageMessages))
                {
                    await e.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder().WithContent(_msg.Get(e.Guild.Id, "Errores:SinPermisos")).AsEphemeral());
                    return;
                }
            }
            
            _sessions.TryRemove(e.Message.Id, out _);
            await e.Message.DeleteAsync();
            await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            return;
        }

        if (e.Id == BtnNext)
        {
            session.Index++;
            if (session.Index >= session.Urls.Count) session.Index = 0;
        }
        else if (e.Id == BtnPrev)
        {
            session.Index--;
            if (session.Index < 0) session.Index = session.Urls.Count - 1;
        }

        var embed = ConstruirEmbed(session.Query, session.Urls, session.Index);
        await e.Interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
            new DiscordInteractionResponseBuilder()
                .AddEmbed(embed)
                .AddComponents(ConstruirBotones()));
    }

    public DiscordEmbed ConstruirEmbed(string query, List<string> urls, int index)
    {
        return new DiscordEmbedBuilder()
            .WithTitle($"🔎 {query}")
            .WithImageUrl(urls[index])
            .WithFooter($"{index + 1} / {urls.Count}")
            .WithColor(DiscordColor.Azure)
            .Build();
    }

    public DiscordButtonComponent[] ConstruirBotones() =>
    [
        new(ButtonStyle.Primary, BtnPrev, "◀️"),
        new(ButtonStyle.Danger, BtnDel, "🗑️"),
        new(ButtonStyle.Primary, BtnNext, "▶️")
    ];
}
