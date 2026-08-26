using System.Text.Json;
using DSharpPlus;
using DSharpPlus.Entities;
using Lavalink4NET;
using Lavalink4NET.Extensions;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.Options;
using Snowflake.Bot.Configuration;
using Snowflake.Bot.Services.Settings;

namespace Snowflake.Bot.Services;

/// <summary>
/// Lógica de reproducción de música sobre Lavalink4NET.
/// </summary>
public sealed class MusicService(
    IAudioService audio,
    IPlayerManager players,
    ITrackManager tracks,
    GuildSettingsService settings,
    IOptionsMonitor<MusicOptions> options,
    IOptionsMonitor<LavalinkOptions> lavalinkOptions,
    IHttpClientFactory httpClientFactory)
{
    /// <summary>
    /// Comprueba de forma ultra-rápida (con timeout de 1.5s) si el servidor Lavalink está online y responde.
    /// </summary>
    public async ValueTask<bool> EstaOnlineAsync(CancellationToken cancellationToken = default)
    {
        var host = lavalinkOptions.CurrentValue.Host;
        var port = lavalinkOptions.CurrentValue.Port;
        var pass = lavalinkOptions.CurrentValue.Password;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(1500));

            var client = httpClientFactory.CreateClient("LavalinkDiag");
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://{host}:{port}/version");
            request.Headers.Add("Authorization", pass);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Recupera el reproductor activo del guild, o null si no hay.</summary>
    public IQueuedLavalinkPlayer? Obtener(ulong guildId)
        => players.TryGetPlayer<IQueuedLavalinkPlayer>(guildId, out var p) ? p : null;

    /// <summary>
    /// Carga y reproduce una búsqueda/URL. Devuelve el resultado del carga y si
    /// la pista acabó encolada (ya había algo reproduciéndose) o sonando ya.
    /// </summary>
    public async Task<(TrackLoadResult Result, bool PuestaEnCola)> ReproducirAsync(
        ulong guildId, ulong voiceChannelId, string consulta)
    {
        if (!await EstaOnlineAsync().ConfigureAwait(false))
            throw new LavalinkUnavailableException("Lavalink está desconectado o no responde.");

        await audio.WaitForReadyAsync(default).ConfigureAwait(false);

        // Si no había reproductor, se une al canal de voz auto-sordo (no oye a los usuarios).
        if (Obtener(guildId) is null)
        {
            // Se lee antes de unirse para que el nuevo reproductor reciba siempre la
            // configuración persistida, incluso cuando todavía no hay una canción.
            var volumenGuardado = await LeerVolumenAsync(guildId).ConfigureAwait(false);

            await players.JoinAsync(
                guildId, voiceChannelId, PlayerFactory.Queued,
                (QueuedLavalinkPlayerOptions o) => o.SelfDeaf = true, default)
                .ConfigureAwait(false);

            if (volumenGuardado is int volumen && Obtener(guildId) is LavalinkPlayer reproductor)
                await reproductor.SetVolumeAsync(volumen / 100f, default).ConfigureAwait(false);
        }

        var player = Obtener(guildId)!;
        var yaTocando = player.CurrentTrack is not null;

        var resultado = await CargarTracksAsync(consulta).ConfigureAwait(false);

        if (resultado.IsSuccess)
            await player.PlayAsync(resultado, default).ConfigureAwait(false);

        // Si ya tocaba algo (y no es playlist entera), la pista fue a la cola.
        var puestaEnCola = yaTocando && resultado.IsSuccess && !resultado.IsPlaylist;
        return (resultado, puestaEnCola);
    }

    /// <summary>Salta n canciones. Devuelve la pista que queda sonando (o null si la cola se vació).</summary>
    public async Task<LavalinkTrack?> SaltarAsync(ulong guildId, int n = 1)
    {
        if (Obtener(guildId) is { } p)
        {
            await p.SkipAsync(n, default).ConfigureAwait(false);
            return p.CurrentTrack;
        }
        return null;
    }

    public async Task PausarAsync(ulong guildId)
    {
        if (Obtener(guildId) is { } p) await p.PauseAsync(default).ConfigureAwait(false);
    }

    public async Task ReanudarAsync(ulong guildId)
    {
        if (Obtener(guildId) is { } p) await p.ResumeAsync(default).ConfigureAwait(false);
    }

    /// <summary>Detiene y desconecta el reproductor del canal de voz.</summary>
    public async Task DetenerAsync(ulong guildId)
    {
        if (Obtener(guildId) is { } p)
        {
            try { await p.StopAsync(default).ConfigureAwait(false); } catch { }
            try { await p.DisconnectAsync(default).ConfigureAwait(false); } catch { }
        }
    }

    /// <summary>
    /// Volumen en porcentaje (se acota con MusicOptions). Se guarda por servidor
    /// aunque no haya un reproductor activo, para que el ajuste también pueda
    /// hacerse antes de usar /m play.
    /// </summary>
    public async Task<int> VolumenAsync(ulong guildId, int porcentaje)
    {
        var min = options.CurrentValue.MinVolume;
        var max = Math.Max(min, options.CurrentValue.MaxVolume);
        porcentaje = Math.Clamp(porcentaje, min, max);

        if (Obtener(guildId) is LavalinkPlayer reproductor)
            await reproductor.SetVolumeAsync(porcentaje / 100f, default).ConfigureAwait(false);

        await settings.UpdateAsync(guildId, cfg => cfg.Volume = porcentaje).ConfigureAwait(false);
        return porcentaje;
    }

    /// <summary>Volumen actual del servidor: el persistido o 100 si nunca se configuró.</summary>
    public async Task<int> ObtenerVolumenActualAsync(ulong guildId)
    {
        var guardado = await LeerVolumenAsync(guildId).ConfigureAwait(false);
        return guardado ?? 100;
    }

    private async Task<int?> LeerVolumenAsync(ulong guildId)
    {
        var cfg = await settings.GetAsync(guildId).ConfigureAwait(false);
        return cfg.Volume;
    }

    private async Task<TrackLoadResult> CargarTracksAsync(string consulta)
    {
        // 1. URLs directas de Spotify
        if (TryGetSpotifyTrackUrl(consulta, out var spotifyUrl))
        {
            var resultadoSpotify = await CargarCancionSpotifyAsync(spotifyUrl).ConfigureAwait(false);
            if (resultadoSpotify is { IsSuccess: true } resultadoValido)
                return resultadoValido;
        }

        // 2. URLs directas (SoundCloud, YouTube, Twitch, HTTP stream, etc.)
        if (Uri.TryCreate(consulta, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return await tracks.LoadTracksAsync(consulta, TrackSearchMode.None, default, default).ConfigureAwait(false);
        }

        // 3. Búsqueda por término: primero en YouTube
        var resYt = await tracks.LoadTracksAsync(
            consulta,
            new TrackLoadOptions { SearchMode = TrackSearchMode.YouTube },
            default, default).ConfigureAwait(false);

        if (resYt.IsSuccess && (resYt.Track is not null || resYt.Playlist is not null))
            return resYt;

        // 4. Fallback automático a SoundCloud si YouTube no encuentra o falla
        return await tracks.LoadTracksAsync(
            consulta,
            new TrackLoadOptions { SearchMode = TrackSearchMode.SoundCloud },
            default, default).ConfigureAwait(false);
    }

    private async Task<TrackLoadResult?> CargarCancionSpotifyAsync(string spotifyUrl)
    {
        try
        {
            var client = httpClientFactory.CreateClient("Spotify");
            var endpoint = "https://open.spotify.com/oembed?url=" + Uri.EscapeDataString(spotifyUrl);
            using var response = await client.GetAsync(endpoint, CancellationToken.None).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(CancellationToken.None).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            if (!json.RootElement.TryGetProperty("title", out var titleElement)) return null;

            var title = titleElement.GetString();
            if (string.IsNullOrWhiteSpace(title)) return null;

            var resYt = await tracks.LoadTracksAsync(
                title,
                new TrackLoadOptions { SearchMode = TrackSearchMode.YouTube },
                default, default).ConfigureAwait(false);

            if (resYt.IsSuccess && (resYt.Track is not null || resYt.Playlist is not null))
                return resYt;

            return await tracks.LoadTracksAsync(
                title,
                new TrackLoadOptions { SearchMode = TrackSearchMode.SoundCloud },
                default, default).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    private static bool TryGetSpotifyTrackUrl(string consulta, out string url)
    {
        url = string.Empty;
        var texto = consulta.Trim();

        if (texto.StartsWith("spotify:track:", StringComparison.OrdinalIgnoreCase))
        {
            var id = texto["spotify:track:".Length..].Trim();
            if (id.Length == 0) return false;
            url = $"https://open.spotify.com/track/{Uri.EscapeDataString(id)}";
            return true;
        }

        if (!Uri.TryCreate(texto, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return false;

        var host = uri.Host.ToLowerInvariant();
        if (host is not ("open.spotify.com" or "play.spotify.com" or "embed.spotify.com"))
            return false;

        var segmentos = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var indiceTrack = Array.FindIndex(segmentos,
            segmento => segmento.Equals("track", StringComparison.OrdinalIgnoreCase));
        if (indiceTrack < 0 || indiceTrack + 1 >= segmentos.Length) return false;

        var trackId = segmentos[indiceTrack + 1].Trim();
        if (trackId.Length == 0) return false;

        url = $"https://open.spotify.com/track/{Uri.EscapeDataString(trackId)}";
        return true;
    }

    /// <summary>
    /// ¿Puede este usuario controlar la música (pausar, saltar, detener)?
    /// Requisitos (cualquiera): ManageGuild, rol DJ configurado o estar en el
    /// MISMO canal de voz que el bot. Devuelve el mensaje de error localizado
    /// cuando no puede. Compartido por /m y el ejecutor de comandos por IA.
    /// </summary>
    public async Task<(bool Puede, string? MensajeError)> ValidarControlAsync(
        DiscordGuild guild, DiscordMember miembro, MessagesService msg)
    {
        if (miembro.Permissions.HasPermission(Permissions.ManageGuild))
            return (true, null);

        var dj = (await settings.GetAsync(guild.Id)).DjRoleId;
        if (dj is { } djId && miembro.Roles.Any(r => r.Id == djId))
            return (true, null);

        var canalBot = guild.CurrentMember?.VoiceState?.Channel;
        var canalUsuario = miembro.VoiceState?.Channel;
        if (canalBot is not null && canalUsuario is not null && canalBot.Id == canalUsuario.Id)
            return (true, null);

        var mensaje = dj is not null
            ? msg.Get(guild.Id, "Musica:RequiereDj", ("rol", $"<@&{dj}>"))
            : msg.Get(guild.Id, "Musica:MismoCanal");
        return (false, mensaje);
    }

    /// <summary>Lista de items en cola (sin contar el que suena ahora).</summary>
    public IEnumerable<ITrackQueueItem> Cola(ulong guildId)
    {
        var p = Obtener(guildId);
        return p is null ? [] : p.Queue;
    }

    /// <summary>Construye el embed de "cola" (sonando ahora + siguientes). Compartido por /m cola y el botón Cola.</summary>
    public DiscordEmbedBuilder? ConstruirEmbedCola(ulong guildId, MessagesService msg)
    {
        var p = Obtener(guildId);
        var actual = p?.CurrentTrack;
        var cola = Cola(guildId).ToList();

        if (actual is null && cola.Count == 0) return null;

        var embed = new DiscordEmbedBuilder()
            .WithTitle(msg.Get(guildId, "Musica:ColaTitulo"))
            .WithColor(DiscordColor.Blurple);

        if (actual is { } ahora)
            embed.AddField(msg.Get(guildId, "Musica:SonandoAhora"),
                $"**[{ahora.Title}]({ahora.Uri})** — {ahora.Author}");

        if (cola.Count > 0)
        {
            var total = TimeSpan.Zero;
            var lineas = new List<string>();
            for (var i = 0; i < cola.Count; i++)
            {
                var t = cola[i].Track!;
                if (!t.IsLiveStream) total += t.Duration;
                lineas.Add($"`{i + 1,2}.` **{t.Title}** — {t.Author}");
            }

            embed.AddField(msg.Get(guildId, "Musica:ColaSiguiente"),
                string.Join("\n", lineas).Truncate(1900));

            if (!total.Equals(TimeSpan.Zero))
                embed.AddField(msg.Get(guildId, "Musica:ColaTotal"), FormatearDuracion(total, false), true);
        }

        return embed;
    }

    /// <summary>URL de la portada de una pista (la del track o, si no, la miniatura de YouTube).</summary>
    public static string? ArtworkUrl(LavalinkTrack track)
    {
        if (!string.IsNullOrEmpty(track.ArtworkUri?.ToString())) return track.ArtworkUri.ToString();
        if (track.SourceName == "youtube" && !string.IsNullOrEmpty(track.Identifier))
            return $"https://i.ytimg.com/vi/{track.Identifier}/hqdefault.jpg";
        return null;
    }

    /// <summary>
    /// Interpreta el nivel de volumen: número absoluto (50), ajuste relativo
    /// (-10, +5 → sobre el volumen actual) o expresión simple de un operador
    /// (30+20, 100/2, 10*3). Devuelve false si no se entiende.
    /// </summary>
    public static bool TryParseVolumen(string entrada, int volumenActual, out int resultado)
    {
        resultado = 0;
        entrada = entrada.Trim();
        if (entrada.Length == 0) return false;

        // Ajuste relativo: empieza por + o - (siempre relativo, nunca absoluto).
        if (entrada[0] is '+' or '-' && long.TryParse(entrada, out var delta))
        {
            resultado = volumenActual + (int)delta;
            return true;
        }

        // Número absoluto.
        if (long.TryParse(entrada, out var absoluto))
        {
            resultado = (int)absoluto;
            return true;
        }

        // Expresión simple: operando operador operando (un solo operador).
        foreach (var op in new[] { '+', '-', '*', '/' })
        {
            var idx = entrada.IndexOf(op);
            if (idx <= 0 || idx >= entrada.Length - 1) continue;
            if (!long.TryParse(entrada[..idx].Trim(), out var a)) continue;
            if (!long.TryParse(entrada[(idx + 1)..].Trim(), out var b)) continue;

            resultado = op switch
            {
                '+' => (int)(a + b),
                '-' => (int)(a - b),
                '*' => (int)(a * b),
                '/' when b != 0 => (int)(a / b),
                _ => resultado
            };
            if (op == '/' && b == 0) return false;
            return true;
        }

        return false;
    }

    public static string FormatearDuracion(TimeSpan d, bool enVivo, string enVivoLabel = "🔴 LIVE")
        => enVivo ? enVivoLabel : d.TotalHours >= 1 ? d.ToString(@"h\:mm\:ss") : d.ToString(@"m\:ss");

    /// <summary>
    /// Diagnóstico del estado del servidor Lavalink (temporal para pruebas de infraestructura).
    /// </summary>
    public async Task<DiscordEmbedBuilder> ConstruirEmbedEstadoLavalinkAsync(ulong guildId, MessagesService msg)
    {
        var cfg = lavalinkOptions.CurrentValue;
        var host = cfg.Host;
        var port = cfg.Port;
        var pass = cfg.Password;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var client = httpClientFactory.CreateClient("LavalinkDiag");

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://{host}:{port}/v4/info");
            request.Headers.Add("Authorization", pass);
            using var response = await client.SendAsync(request).ConfigureAwait(false);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return new DiscordEmbedBuilder()
                    .WithTitle("🔌 Lavalink Status — Error")
                    .WithColor(DiscordColor.Red)
                    .AddField("Host", $"`{host}:{port}`", inline: true)
                    .AddField("Estado", $"❌ HTTP {(int)response.StatusCode} {response.ReasonPhrase}", inline: true)
                    .AddField("Latencia", $"`{sw.ElapsedMilliseconds} ms`", inline: true)
                    .WithFooter("Error al autenticar o consultar Lavalink");
            }

            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            var root = doc.RootElement;

            var version = root.TryGetProperty("version", out var vObj) && vObj.TryGetProperty("semver", out var semver)
                ? semver.GetString() ?? "v4"
                : "v4";

            var jvm = root.TryGetProperty("jvm", out var jvmProp) ? jvmProp.GetString() : "Unknown";

            var pluginsList = new List<string>();
            if (root.TryGetProperty("plugins", out var pluginsProp) && pluginsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in pluginsProp.EnumerateArray())
                {
                    if (p.TryGetProperty("name", out var pName) && p.TryGetProperty("version", out var pVer))
                        pluginsList.Add($"{pName.GetString()} ({pVer.GetString()})");
                }
            }

            // Consultar /v4/stats
            var reqStats = new HttpRequestMessage(HttpMethod.Get, $"http://{host}:{port}/v4/stats");
            reqStats.Headers.Add("Authorization", pass);
            using var resStats = await client.SendAsync(reqStats).ConfigureAwait(false);

            int totalPlayers = 0;
            int playingPlayers = 0;
            TimeSpan uptime = TimeSpan.Zero;
            long ramUsedMb = 0;
            long ramAllocatedMb = 0;
            double cpuLav = 0.0;
            double cpuSys = 0.0;

            if (resStats.IsSuccessStatusCode)
            {
                using var streamStats = await resStats.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var docStats = await JsonDocument.ParseAsync(streamStats).ConfigureAwait(false);
                var stats = docStats.RootElement;

                if (stats.TryGetProperty("players", out var pl)) totalPlayers = pl.GetInt32();
                if (stats.TryGetProperty("playingPlayers", out var ppl)) playingPlayers = ppl.GetInt32();
                if (stats.TryGetProperty("uptime", out var up)) uptime = TimeSpan.FromMilliseconds(up.GetInt64());

                if (stats.TryGetProperty("memory", out var mem))
                {
                    if (mem.TryGetProperty("used", out var u)) ramUsedMb = u.GetInt64() / (1024 * 1024);
                    if (mem.TryGetProperty("allocated", out var al)) ramAllocatedMb = al.GetInt64() / (1024 * 1024);
                }

                if (stats.TryGetProperty("cpu", out var cpu))
                {
                    if (cpu.TryGetProperty("lavalinkLoad", out var cl)) cpuLav = cl.GetDouble() * 100;
                    if (cpu.TryGetProperty("systemLoad", out var cs)) cpuSys = cs.GetDouble() * 100;
                }
            }

            var embed = new DiscordEmbedBuilder()
                .WithTitle("🔌 Lavalink Status — Online")
                .WithColor(DiscordColor.SpringGreen)
                .AddField("🟢 Estado", "Conectado y Operativo", inline: true)
                .AddField("📡 Host", $"`{host}:{port}`", inline: true)
                .AddField("⚡ Latencia REST", $"`{sw.ElapsedMilliseconds} ms`", inline: true)
                .AddField("📦 Versión", $"`{version}` (JVM: {jvm})", inline: true)
                .AddField("⏱️ Uptime", $"`{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s`", inline: true)
                .AddField("🎵 Reproductores", $"Tocando: `{playingPlayers}` / Total: `{totalPlayers}`", inline: true)
                .AddField("💾 Memoria RAM", $"`{ramUsedMb} MB` / `{ramAllocatedMb} MB`", inline: true)
                .AddField("🖥️ CPU Load", $"Lavalink: `{cpuLav:F1}%` / Sistema: `{cpuSys:F1}%`", inline: true)
                .AddField("🧩 Plugins", pluginsList.Count > 0 ? string.Join(", ", pluginsList) : "Ninguno", inline: false)
                .WithFooter($"Consultado por Snowflake • {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");

            return embed;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new DiscordEmbedBuilder()
                .WithTitle("🔌 Lavalink Status — Offline")
                .WithColor(DiscordColor.Red)
                .AddField("🔴 Estado", "Inalcanzable / Desconectado", inline: true)
                .AddField("📡 Host", $"`{host}:{port}`", inline: true)
                .AddField("⏱️ Tiempo intento", $"`{sw.ElapsedMilliseconds} ms`", inline: true)
                .AddField("⚠️ Detalle del error", $"```{ex.GetType().Name}: {ex.Message}```", inline: false)
                .WithFooter("Verifica que el servidor de Lavalink esté encendido y que el puerto sea accesible.");
        }
    }
}

public sealed class LavalinkUnavailableException(string message) : Exception(message);

public static class ColaExtensions
{
    public static string Truncate(this string s, int max)
        => s.Length <= max ? s : s[..(max - 1)] + "…";
}