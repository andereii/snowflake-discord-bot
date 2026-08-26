using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Snowflake.Bot.Utilities;

namespace Snowflake.Bot.Services;

public sealed class PollWidgetService
{
    private readonly MessagesService _msg;
    private readonly ILogger<PollWidgetService> _logger;

    public static readonly DiscordEmoji[] NumberEmojis =
    [
        DiscordEmoji.FromUnicode("1️⃣"),
        DiscordEmoji.FromUnicode("2️⃣"),
        DiscordEmoji.FromUnicode("3️⃣"),
        DiscordEmoji.FromUnicode("4️⃣"),
        DiscordEmoji.FromUnicode("5️⃣"),
        DiscordEmoji.FromUnicode("6️⃣"),
        DiscordEmoji.FromUnicode("7️⃣"),
        DiscordEmoji.FromUnicode("8️⃣"),
        DiscordEmoji.FromUnicode("9️⃣"),
        DiscordEmoji.FromUnicode("🔟")
    ];

    private sealed class PollSession
    {
        public ulong MessageId { get; set; }
        public ulong ChannelId { get; set; }
        public ulong GuildId { get; set; }
        public ulong AuthorId { get; set; }
        public string Question { get; set; } = "";
        public List<string> Options { get; set; } = new();
        public bool MultiOption { get; set; }
        
        // UserId -> Set de índices de opción (0-9) a los que ha votado
        public ConcurrentDictionary<ulong, HashSet<int>> Votes { get; set; } = new();
        
        public CancellationTokenSource? Cts { get; set; }
    }

    private readonly ConcurrentDictionary<ulong, PollSession> _polls = new();

    public PollWidgetService(MessagesService msg, ILogger<PollWidgetService> logger)
    {
        _msg = msg;
        _logger = logger;
    }

    public async Task RegistrarEncuestaAsync(DiscordMessage msg, ulong authorId, string question, List<string> options, bool multiOption, int minutos)
    {
        var session = new PollSession
        {
            MessageId = msg.Id,
            ChannelId = msg.Channel.Id,
            GuildId = msg.Channel.Guild.Id,
            AuthorId = authorId,
            Question = question,
            Options = options,
            MultiOption = multiOption
        };

        if (minutos > 0)
        {
            session.Cts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(minutos), session.Cts.Token);
                    await FinalizarEncuestaAsync(msg.Channel.Guild, msg.Channel, msg.Id);
                }
                catch (TaskCanceledException) { /* Finalizada manualmente */ }
            });
        }

        _polls[msg.Id] = session;

        // Añadir reacciones
        for (int i = 0; i < options.Count && i < 10; i++)
        {
            await msg.CreateReactionAsync(NumberEmojis[i]);
            await Task.Delay(200); // Prevenir rate limit
        }
    }

    public async Task ManejarReaccionAgregadaAsync(MessageReactionAddEventArgs e)
    {
        if (e.User.IsBot) return;
        if (!_polls.TryGetValue(e.Message.Id, out var session)) return;

        int optIndex = Array.IndexOf(NumberEmojis, e.Emoji);
        if (optIndex == -1 || optIndex >= session.Options.Count) return;

        session.Votes.AddOrUpdate(e.User.Id, 
            _ => new HashSet<int> { optIndex },
            (_, set) => 
            {
                if (!session.MultiOption && set.Count >= 1 && !set.Contains(optIndex))
                {
                    // Es single-choice y ya tiene un voto distinto, ignoramos silenciosamente
                    return set;
                }
                set.Add(optIndex);
                return set;
            });
    }

    public async Task ManejarReaccionRemovidaAsync(MessageReactionRemoveEventArgs e)
    {
        if (e.User.IsBot) return;
        if (!_polls.TryGetValue(e.Message.Id, out var session)) return;

        int optIndex = Array.IndexOf(NumberEmojis, e.Emoji);
        if (optIndex == -1 || optIndex >= session.Options.Count) return;

        if (session.Votes.TryGetValue(e.User.Id, out var set))
        {
            set.Remove(optIndex);
        }
    }

    public async Task<bool> IntentarFinalizarManualAsync(DiscordMessage msg, DiscordUser user, DiscordGuild guild)
    {
        if (!_polls.TryGetValue(msg.Id, out var session)) return false;

        if (session.AuthorId != user.Id)
        {
            var member = await guild.GetMemberAsync(user.Id);
            if (!member.Permissions.HasPermission(Permissions.ManageMessages))
            {
                return false;
            }
        }

        session.Cts?.Cancel();
        await FinalizarEncuestaAsync(guild, msg.Channel, msg.Id);
        return true;
    }

    private async Task FinalizarEncuestaAsync(DiscordGuild guild, DiscordChannel channel, ulong messageId)
    {
        if (!_polls.TryRemove(messageId, out var session)) return;

        // Conteo
        var resultados = new int[session.Options.Count];
        foreach (var userVotes in session.Votes.Values)
        {
            foreach (var vote in userVotes)
            {
                if (vote >= 0 && vote < resultados.Length)
                {
                    resultados[vote]++;
                }
            }
        }

        // Generar Pie Chart
        string imagePath = await GenerarPieChartAsync(session.Options, resultados);
        
        try
        {
            var msg = await channel.GetMessageAsync(messageId);
            
            var embed = new DiscordEmbedBuilder(msg.Embeds[0])
                .WithColor(DiscordColor.Green)
                .WithDescription(_msg.Get(guild.Id, "Encuestas:FinalizadaDesc"));

            var builder = new DiscordMessageBuilder().WithContent(msg.Content);

            if (imagePath != null && File.Exists(imagePath))
            {
                using var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
                builder.AddFile("chart.png", fs);
                embed.WithImageUrl("attachment://chart.png");
                builder.AddEmbed(embed);
                await msg.ModifyAsync(builder);
            }
            else
            {
                builder.AddEmbed(embed);
                await msg.ModifyAsync(builder);
            }

            try { await msg.DeleteAllReactionsAsync(); } catch { /* Faltan permisos, ignorar */ }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al finalizar encuesta {MessageId}", messageId);
        }
        finally
        {
            if (imagePath != null && File.Exists(imagePath))
            {
                try { File.Delete(imagePath); } catch { }
            }
        }
    }

    private async Task<string?> GenerarPieChartAsync(List<string> labels, int[] values)
    {
        try
        {
            var json = JsonSerializer.Serialize(new { labels, values });
            string tmpFile = Path.GetTempFileName() + ".png";

            // En Fly.io estará en /app/Dlang/piechart
            // Localmente en desarrollo puede estar en src/Dlang/piechart
            string binPath = File.Exists("/app/Dlang/piechart") 
                ? "/app/Dlang/piechart" 
                : Path.Combine(AppContext.BaseDirectory, "../../../../Dlang/piechart");

            if (!File.Exists(binPath)) binPath = "src/Dlang/piechart"; // Fallback absoluto para dev

            var psi = new ProcessStartInfo
            {
                FileName = binPath,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(tmpFile);

            using var process = Process.Start(psi);
            if (process == null) return null;

            await process.StandardInput.WriteAsync(json);
            process.StandardInput.Close();

            await process.WaitForExitAsync();

            var stderr = await process.StandardError.ReadToEndAsync();
            if (process.ExitCode != 0)
            {
                _logger.LogWarning(
                    "El ejecutable de Dlang terminó con código {Codigo}: {Error}",
                    process.ExitCode, stderr);
                return null;
            }

            if (!File.Exists(tmpFile))
            {
                _logger.LogWarning("El ejecutable de Dlang no generó el archivo {Archivo}", tmpFile);
                return null;
            }

            return tmpFile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error llamando al ejecutable de Dlang");
            return null;
        }
    }
}
