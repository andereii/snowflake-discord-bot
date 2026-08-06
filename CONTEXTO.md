# 📋 Dossier completo: Snowflake — Bot de Discord

> Documento de contexto para compartir el estado completo del proyecto con otra IA o desarrollador.
> Fecha: agosto 2026.

## 1. Resumen
Bot de Discord en **C# / .NET 10** con **DSharpPlus 5.0.0** que ofrece: moderación documentada, bienvenidas, descarga de vídeos/audio, paletas de colores autoasignables, creación de canales, música vía Lavalink, sistema join-to-create de canales de voz, **juego de conteo** (con bases alternativas, récords, oportunidades extra y leaderboard), **chatbot con Gemini** (vía `/charlar`, mención `@` y modo espontáneo) y **notificaciones de YouTube** vía feed RSS público. El objetivo final es desplegarlo en un VPS propio. Todo el código está en español.

- **Nombre:** Snowflake (`Snowflake#3104`)
- **Client ID:** `1052318909035970641`
- **Guild de pruebas:** `1475204967567589440` (los comandos se registran SOLO aquí, para que aparezcan al instante)
- **Owner ID:** `553023040489914369`
- **Intents:** Guilds, GuildMembers, GuildBans, GuildVoiceStates, GuildMessages, **MessageContents** (necesario para conteo, menciones y cháchara espontánea)
- **Endpoint de interacciones:** vacío (funciona por gateway, no por HTTP)

## 2. Stack y versiones
| Componente | Versión |
|---|---|
| .NET SDK | 10.0.110 (net10.0) |
| DSharpPlus + DSharpPlus.SlashCommands | 5.0.0 estable |
| Lavalink4NET.DSharpPlus | 4.2.2 |
| EF Core Sqlite + Design | 10.0.10 |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.12 (parchea aviso de vulnerabilidad) |
| DotNetEnv | 3.2.0 |
| Microsoft.Extensions.Hosting | 10.0.10 |
| Lavalink (servidor Java) | 4.2.2 (jar en deploy/lavalink/, Java 17+) |
| yt-dlp | 2026.07.04 (instalado en el sistema) |
| ffmpeg | 8.1.2 (requerido por yt-dlp para audio) |

**Herramientas:** `dotnet-ef` global 10.0.10, `Microsoft.EntityFrameworkCore.Design` (paquete).
**Máquina:** CachyOS (Arch). Docker NO instalado.

## 3. Estructura del proyecto
```
snowflake_discord_bot/
├── .env / .env.example      # DISCORD_TOKEN (obligatorio), YT_COOKIES_FILE, GEMINI_API_KEY, SPOTIFY_CLIENT_ID/SECRET (opcional)
├── deploy/lavalink/
│   ├── Lavalink.jar         # servidor 4.2.2
│   ├── application.yml      # config + plugins (youtube-source + lavasrc)
│   └── run.sh               # java -jar Lavalink.jar (carga .env antes)
└── src/Snowflake.Bot/
    ├── Snowflake.Bot.csproj
    ├── Program.cs           # host, DI, arranque
    ├── appsettings.json     # Bot:{TestGuildId,OwnerId,Debug}, Lavalink, Gemini
    ├── messages.json        # TODOS los textos del bot (recarga en caliente)
    ├── Configuration/       # BotConfiguration, LavalinkOptions, GeminiOptions
    ├── Data/
    │   ├── BotDbContext.cs
    │   ├── BotDbContextFactory.cs  # IDesignTimeDbContextFactory para EF Migrations
    │   ├── Entities/        # Incident, GuildConfig, ColorRole, TempChannel, CountingConfig, CountingStat, YouTubeSubscription
    │   └── Migrations/      # InitialCreate, AddCounting, AddGeminiMentions, AddGeminiSpontaneous, AddYouTubeSubscriptions
    ├── Modules/             # comandos slash (8 módulos)
    ├── Services/            # lógica de negocio (11 servicios)
    └── Utilities/           # DurationParser, ChatResponseFormatter
```

**Ojo:** el repositorio git real es `/home/alex` (todo el home está sin commitear, rama `master` sin commits). El proyecto NO tiene git propio.

## 4. Arquitectura de arranque (`Program.cs`)
1. `Env.TraversePath().Load()` — carga `.env` (no sobreescribe vars del sistema).
2. Host genérico con `ContentRootPath = AppContext.BaseDirectory` (la config se copia al ejecutable, funciona desde cualquier cwd).
3. `Bot` → `IOptionsMonitor<BotConfiguration>` (permite hot-reload de `Debug`); `Gemini` → `IOptionsMonitor<GeminiOptions>`.
4. `messages.json` → `IConfiguration` con `reloadOnChange: true` + `MessagesService`.
5. `IDbContextFactory<BotDbContext>` → SQLite `snowflake.db` junto al ejecutable.
6. `AddHttpClient` nombrados: `"Spotify"` (15s, fallback de canciones), `"Gemini"` (60s, generacion), `"YouTube"` (15s, feed RSS).
7. `AddLavalink()` + `ConfigureLavalink` (`BaseAddress = http://host:port`, `Passphrase`, `Label = "snowflake"`).
8. Servicios singleton: ModerationLog, Download, Litterbox, Color, VoiceHub, Music, MusicWidget, Counting, Gemini, YouTubeNotify.
9. `AddHostedService<DiscordBotService>` (host) y `AddHostedService<YouTubeNotifyService>` (background de polling RSS).
10. `DiscordClient` con los intents incluido `MessageContents` (token de `DISCORD_TOKEN`, lanza excepción clara si falta).
11. `DiscordBotService` registra los módulos slash con `UseSlashCommands` y `RegisterCommands(assembly, TestGuildId)`.
12. Al arrancar: `db.Database.MigrateAsync()` (EF Migrations; se migró desde `EnsureCreatedAsync`).

## 5. Base de datos SQLite (EF Core)
Tablas (migraciones aplicadas en orden: `InitialCreate`, `AddCounting`, `AddGeminiMentions`, `AddGeminiSpontaneous`, `AddYouTubeSubscriptions`):

**`Incidents`** — historial de moderación (número de caso autoincremental):
`Id`, `GuildId`, `TargetUserId`, `TargetTag`, `ModeratorId`, `ModeratorTag`, `Type` (enum guardado como string: Advertencia/Expulsion/Veto/Aislamiento/FinAislamiento), `Reason`, `Duration TimeSpan?` (solo aislamientos), `CreatedAt`. Índice `(GuildId, TargetUserId)`.

**`GuildConfigs`** — config por servidor (PK `GuildId`):
`ModLogChannelId ulong?`, `WelcomeChannelId ulong?`, `WelcomeMessage string?`, `HubChannelId ulong?`, `Volume int?` (0-100, persistente, null = 100 por defecto), `GeminiMentionsEnabled bool` (toggle de respuestas a `@`), `GeminiSpontaneousEnabled bool` (toggle de cháchara espontánea).

**`ColorRoles`** — roles de color instalados:
`Id`, `GuildId`, `RoleId`, `Name`, `ColorHex`. Único `(GuildId, RoleId)`.

**`TempChannels`** — canales temporales join-to-create:
`ChannelId` (PK), `GuildId`, `OwnerUserId`, `CreatedAt`.

**`CountingConfigs`** — config del juego de conteo (PK `GuildId`):
`ChannelId ulong?`, `CurrentValue long` (almacenado en decimal), `LastUserId ulong?` (no contar dos veces seguidas), `CurrentRecord long` (récord histórico), `RecordAtChainStart long`, `RecordCelebratedThisChain bool`, `Base` (enum string: Decimal/Binario/Octal/Hexadecimal), `Goal long?`, `ExtraChancesPerDay int` (0-10), `ExtraChancesUsedToday int`, `LastExtraChanceResetDate string?` ("yyyy-MM-dd" UTC), `EmojiCorrect/EmojiIncorrect/EmojiRecord string?` (null = ✅/❌/🎉), `LoseMessage string?` (placeholders `{cuenta}` `{usuario}` `{siguiente}`).

**`CountingStats`** — estadísticas por usuario:
`Id`, `GuildId`, `UserId`, `TotalCounts long`, `IncorrectCounts long`, `BestContribution long`. Único `(GuildId, UserId)`, índice `GuildId`.

**`YouTubeSubscriptions`** — suscripción de YouTube por servidor (PK `GuildId`, una por server):
`YTChannelId string` (UC…), `YTChannelName string`, `NotifyChannelId ulong`, `NotifyRoleId ulong?`, `LastVideoId string?` (marca de backfill), `CustomMessage string?` (plantilla, null = por defecto), `CreatedAt`. Índice `YTChannelId` (agrupa servidores que siguen el mismo canal).

**Gotcha conocido:** con `EnsureCreated` la BD no se actualiza al añadir columnas (hubo que borrarla a mano dos veces); por eso se pasó a EF Migrations. Tras crear una migración con `dotnet ef migrations add` hay que **reconstruir** (`dotnet build`) antes de ejecutar la DLL: si no, EF Core 10 lanza `PendingModelChangesWarning` porque el ensamblado no incluye el snapshot actualizado.

## 6. Sistema de textos (`messages.json` + `MessagesService`)
- Todos los mensajes viven en `messages.json` con estructura de secciones: `Ping`, `Errores`, `Presentacion`, `Moderacion`, `Descargas`, `Bienvenida`, `Musica`, `Colores`, `Voces`, `Config`, `Chat`, `Conteo`, `YouTube`.
- Se accede por clave con `:` separando niveles: `msg.Get("Musica:NoEncontrado")`, con placeholders: `msg.Get("Bienvenida:MensajePorDefecto", ("usuario", mention), ("servidor", nombre))`.
- Placeholders en el archivo van entre llaves: `{usuario}`, `{servidor}`, `{motivo}`, `{titulo}`, `{duracion}`, `{nivel}`, etc.
- `reloadOnChange: true` → editar el JSON en caliente cambia los textos al instante, sin reiniciar (los nombres/descripciones de comandos SÍ están en código).
- Si una clave no existe devuelve `⚠️ Mensaje no encontrado: \`clave\``.

## 7. Comandos slash (todos registrados en el guild de pruebas)

| Comando | Permiso | Función |
|---|---|---|
| `/ping` | todos | Latencia |
| `/canal-logs` `{canal}` | ManageGuild | Canal de anuncios de moderación |
| `/expulsar` `{usuario,motivo?}` | KickMembers | Expulsa + DM de aviso + caso + log |
| `/vetar` `{usuario,motivo?,borrar_dias?}` | BanMembers | Banea (funciona aunque no esté en el server) |
| `/aislar` `{usuario,duracion,motivo?}` | ModerateMembers | Timeout (duraciones `30s/10m/2h/7d`, máx 28 días) |
| `/desaislar` `{usuario,motivo?}` | ModerateMembers | Quita timeout |
| `/advertir` `{usuario,motivo}` | ModerateMembers | Advertencia documentada |
| `/historial` `{usuario?}` | ModerateMembers | Últimos 10 incidentes del server o del usuario (efímero) |
| `/bienvenida canal/mensaje/ver/desactivar` | ManageGuild | Bienvenidas (placeholders `{usuario}` `{servidor}`, máx 1900) |
| `/descargar` `{url, formato: Vídeo\|Solo audio}` | todos | yt-dlp: <9MiB se adjunta, >9MiB → litterbox 72h |
| `/canal crear` `{nombre,tipo voz\|texto,categoria?}` | ManageChannels | Crea canal |
| `/canal hub` `{canal}` | ManageGuild | Activa join-to-create en ese canal de voz |
| `/canal hub-quitar` | ManageGuild | Lo desactiva |
| `/colores instalar` `{paleta: normal\|pastel}` | ManageRoles | Crea los 17 roles de color |
| `/colores desinstalar` | ManageRoles | Borra todos los roles de color |
| `/colores elegir` | todos | Menú de selección efímero (custom_id `snowflake_colores`, opción "Quitar color" valor `0`) |
| `/colores quitar` | todos | Te quita el color |
| `/colores listar` | todos | Lista colores instalados (efímero) |
| `/m play` `{consulta}` | todos | URL o búsqueda (YouTube/Spotify) → reproduce o encola + widget |
| `/m skip` | todos | Salta, avisa de la siguiente o de cola vacía |
| `/m cola` | todos | Embed: sonando ahora + siguientes + duración total |
| `/m pausa` `/m reanuda` `/m stop` `/m volumen {nivel}` | todos | Control + volumen persistente por server |
| `/charlar` `{texto}` | todos | Habla con Gemini (conversación compartida por server) |
| `/charlar-limpiar` | todos | Reinicia la conversación compartida |
| `/gemini-menciones` `{estado on/off?}` | ManageGuild | Toggle: ¿responder al ser mencionado con @? |
| `/gemini-espontaneo` `{estado on/off?}` | ManageGuild | Toggle: ¿intervenir solo en el chat cada ~100+rand(1..50) mensajes? |
| `/counting canal` `{canal}` | ManageGuild | Enlaza el canal de conteo (1 canal por server) |
| `/counting desactivar` | ManageGuild | Desenlaza el canal (deja de leer) |
| `/counting base` `{decimal/binario/octal/hexadecimal}` | ManageGuild | Modo de juego (base numérica) |
| `/counting oportunidades` `{0-10}` | ManageGuild | Perdones diarios (0 = desactivado) |
| `/counting objetivo` `{numero}` / `objetivo-quitar` | ManageGuild | Meta del servidor |
| `/counting iconos` `{correcto?,incorrecto?,record?}` | ManageGuild | Emojis de reacción (unicode o `:nombre:id`) |
| `/counting mensaje-perdida` `{mensaje?}` | ManageGuild | Texto al perder (placeholders `{cuenta}` `{usuario}` `{siguiente}`; vacío = por defecto) |
| `/counting leaderboard` | todos | Top 10 aportantes |
| `/counting estadisticas` `{usuario?}` | todos | Totales, precisión, mejor aporte (vacío = tú mismo) |
| `/youtube seguir` `{canal, notificar, rol?}` | ManageGuild | Suscribe al bot a un canal de YT (URL o @handle); resuelve con yt-dlp, hace backfill silencioso |
| `/youtube dejar` | ManageGuild | Elimina suscripción |
| `/youtube ver` | todos | Muestra la suscripción actual |
| `/youtube rol` `{rol?}` | ManageGuild | Cambia/quita rol a mencionar |
| `/youtube canal` `{canal}` | ManageGuild | Cambia canal de Discord donde avisar |
| `/youtube mensaje` `{mensaje?}` | ManageGuild | Personaliza texto previo al enlace (placeholders `{canal}` `{titulo}` `{autor}` `{url}` `{videoId}` `{subido}` `{subidoREL}`; vacío = por defecto) |

**Toggles de Gemini:** requieren `GEMINI_API_KEY` en `.env`. Si falta, no deja activar.

**Reglas de moderación (importantes):** no auto-acciones, no al bot, no al owner, jerarquía de roles (`miembro.Hierarchy >= bot.Hierarchy` → error), DM de aviso best-effort (si tiene MD cerrados sigue), cada acción crea un `Incident` y se anuncia en el canal de logs con embed de color por tipo + número de caso + timestamp.

## 8. Servicios y eventos

**`DiscordBotService`** (BackgroundService, el corazón):
- `Ready`, `GuildDownloadCompleted` (logs + **precarga de flags espontáneo en caché**), `GuildCreated` → presenta al bot en el system channel o primer canal de texto donde pueda escribir.
- `GuildMemberAdded` → bienvenida (usa config de BD o mensaje por defecto; ignoran bots).
- `VoiceStateUpdated` → delega en `VoiceHubService`.
- `ComponentInteractionCreated` → **router de componentes**: `snowflake_colores` → ColorService, `snowflake_music_*` → MusicWidgetService.
- `MessageCreated` → tres caminos: (1) respuesta a un mensaje de Gemini generado por `/charlar` → continúa la conversación; (2) **mención `@` al bot** si `GeminiMentionsEnabled` → responde con Gemini; (3) **cháchara espontánea** si `GeminiSpontaneousEnabled` → cuenta el mensaje y dispara un comentario por el canal al alcanzar el umbral.
- `SlashCommandErrored` → si es `SlashExecutionChecksFailedException` → "SinPermisos"; si no, mensaje de error con detalles solo en modo `Debug`.

**`VoiceHubService`** (join-to-create): al entrar al canal hub → crea canal `🎧 {usuario}` en la misma categoría, con overwrites al dueño (ManageChannels, MoveMembers, MuteMembers, DeafenMembers, AccessChannels, UseVoice), lo mueve dentro y lo registra en `TempChannels`. Al salir de un canal temporal → si queda vacío lo borra y elimina el registro. Ignora cambios de mute/deafen.

**`ColorService`**: dos paletas de 17 colores hardcodeadas (Normal: Rojo→Negro; Pastel: Rosa pastel→Perla). `InstalarAsync` reemplaza paletas cruzadas (borra roles de la otra paleta y crea los que falten). Roles con prefijo `• `. `HandleSelectAsync` gestiona el menú (quita el color anterior, asigna el nuevo).

**`MusicService`**: lógica Lavalink4NET (ver sección 9). Inyecta `IDbContextFactory<BotDbContext>` para persistir/aplicar volumen.

**`MusicWidgetService`**: widget "reproduciendo ahora" (embed + 4 botones: ⏯️ pausa, ⏭️ skip, 📋 cola, ⏹️ stop; custom_ids `snowflake_music_pause/skip/cola/stop`). Un widget por guild, guardado en `ConcurrentDictionary<guildId, (messageId, channelId)>`. **No se autoactualiza** — solo se refresca ante acciones del usuario (decisión de diseño deliberada, sin barra de progreso viva). Stop deja el widget estático con botones deshabilitados. El botón cola responde efímero.

**`CountingService`** (juego de conteo): parsee en base configurada (`IntentarParsear`/`Formatear` con `Convert.ToString(value, radix)`), semáforo por guild contra carreras, persistencia de config/stats. Detecta: correcto (reacciona ✅/🎉 y actualiza récord), incorrecto normal (❌ + reset), **primera vez perdonada** (🛡️ + DM hint privado con el número correcto, fallback a reply si MD cerrados), oportunidades extra diarias (🛡️, reset diario UTC), mismo usuario dos veces (reset). Construye embeds de leaderboard (top 10 con medallas) y stats (totales, precisión, mejor aporte en la base activa).

**`GeminiService`** (chatbot): API gratuita de Google Gemini (`v1beta/models/{model}:generateContent`). Conversación compartida por servidor (histórico acotado por `MaxHistoryTurns`), serialización por `SemaphoreSlim` + límite de 2 solicitudes simultáneas por guild (`Conversacion`). `_mensajesGenerados` (messageId→guildId) para detectar respuestas a mensajes del bot. `Limpiar` reinicia. **Modo espontáneo:** `EstadoEspontaneo` por guild con umbral `100 + rand(1..50)` y cola de últimos 15 mensajes ambientales; `GenerarComentarioEspontaneoAsync` llama a Gemini con un prompt casual sin tocar la conversación de `/charlar`. Refactor `LlamarAGeminiAsync` compartido por `PreguntarAsync` y el espontáneo. Persona de gemini, errores `GeminiException`/`GeminiBusyException`.

**`YouTubeNotifyService`** (BackgroundService): polling del feed RSS público `https://www.youtube.com/feeds/videos.xml?channel_id={UC}` cada 5 min (sin API key). Agrupa suscripciones por `YTChannelId` (un fetch por canal), parsea XML con `XDocument`, compara `LastVideoId`. **Backfill silencioso** al crear suscripción (marca el último vídeo como visto, no notifica antiguos). Resolución de URL/`@handle` a `channel_id` con `yt-dlp --print channel_id` (+`channel` para el nombre). Notificación: texto (personalizado o por defecto) + embed con título, autor, miniatura `i.ytimg.com/vi/{id}/hqdefault.jpg`, timestamp, y mención de rol opcional (`RoleMention` para que el ping funcione). Errores por canal aislados (un feed caído no aborta el resto).

**`DownloadService`**: lanza `yt-dlp` como proceso externo con args `--no-playlist --no-progress --no-warnings --no-part --restrict-filenames --print after_move:filepath -o {plantilla}`, cookies opcionales de `YT_COOKIES_FILE`, modo audio = `-x --audio-format mp3 --audio-quality 0`. Timeout duro 5 min, plantilla `%(title).80B [%(id)s].%(ext)s` en `/tmp/snowflake/{guid}/`. Errores → `YtDlpException` con las últimas 3 líneas de stderr saneadas (máx 800 chars). El llamador limpia el temp dir en `finally`.

**`LitterboxService`**: multipart POST a `litterbox.catbox.moe` con `reqtype=fileupload`, `time=72h`. Devuelve la URL (validada).

**`ModerationLogService`**: `RegistrarAsync` guarda el Incident (devuelve con Id), `AnunciarAsync` lo publica si hay canal de logs, `CrearEmbedIncidente` (colores por tipo: amarillo/naranja/rojo/morado/verde).

**`DurationParser`**: regex `^(\d+)\s*([smhd])$`, formato legible "3 día(s) / 2 hora(s) / 15 minuto(s) / 30 segundo(s)".

**`MessagesService`**: ver sección 6.

**`ChatResponseFormatter`** (Utilities): formatea las respuestas de Gemini antes de enviarlas.

## 9. Música — Lavalink (la parte más delicada)

**Servidor:** `deploy/lavalink/application.yml`:
- `server.port 2333`, `address 127.0.0.1`, password `youshallnotpass`.
- `lavalink.sources.youtube: false` (el fuente nativo de Lavaplayer está roto → HTTP 400).
- Plugin **youtube-source** `dev.lavalink.youtube:youtube-plugin:1.18.2` (descargado automáticamente de maven.lavalink.dev), clients `MUSIC, ANDROID_VR, WEB, WEBEMBEDDED`.
- Plugin **LavaSrc** `com.github.topi314.lavasrc:lavasrc-plugin:4.8.3` (Spotify; mirroring por ISRC → búsqueda YouTube). En la práctica, el token anónimo de Spotify puede fallar (`Failed to retrieve secret from Spotify`), por lo que las playlists/álbumes requieren `SPOTIFY_CLIENT_ID` y `SPOTIFY_CLIENT_SECRET` en `.env`; las canciones individuales tienen un fallback en el bot.

**Lavalink4NET — API real descubierta por reflexión** (proyecto de sondeo en `/tmp/opencode/lavalink-probe`):
- DI: `AddLavalink()` y `ConfigureLavalink(o => { o.BaseAddress; o.Passphrase; o.Label })` — espacio de nombres `Lavalink4NET.Extensions`.
- `IPlayerManager players` (inyectable): `players.TryGetPlayer<IQueuedLavalinkPlayer>(guildId, out var p)`, `players.JoinAsync(guildId, voiceChannelId, PlayerFactory.Queued, (QueuedLavalinkPlayerOptions o) => o.SelfDeaf = true, default)`.
- `ITrackManager tracks` (inyectable): `tracks.LoadTracksAsync(consulta, new TrackLoadOptions { SearchMode = TrackSearchMode.YouTube }, default, default)` → `TrackLoadResult` (`IsSuccess`, `Track`, `IsPlaylist`, `Playlist`, `Count`).
- `player.PlayAsync(resultado, ct)` (extension), `SkipAsync(n, ct)`, `PauseAsync/ResumeAsync/StopAsync/DisconnectAsync`.
- **Gotchas:** `Position` es `TrackPosition?` (usar `?.Position`); `IsPaused` y `SetVolumeAsync` solo existen en la clase concreta `LavalinkPlayer` → hay que castear `((LavalinkPlayer)p)`. `SetVolumeAsync` recibe fracción 0..1, no porcentaje.
- `CurrentTrack` → `LavalinkTrack` con `Title`, `Author`, `Uri`, `Duration`, `IsLiveStream`, `SourceName`, `Identifier`, `ArtworkUri`.

**Flujo de `/m play`:** verifica canal de voz → defer → `ReproducirAsync`: espera `audio.WaitForReadyAsync`; si no hay player, lee de BD el volumen persistido, se une auto-sordo y lo aplica; para una URL de canción de Spotify consulta oEmbed y busca el título en YouTube (fallback sin credenciales); para YouTube/búsquedas y playlists/álbumes usa Lavalink/LavaSrc; si `IsSuccess` → `PlayAsync`; si ya sonaba (y no playlist) → "añadida a la cola" con miniatura; si no → "reproduciendo" + widget. Playlist → "playlist añadida con N pistas". Errores → "ErrorLavalink".

**Widget:** embed con título/url, autor, miniatura (ArtworkUri o fallback `https://i.ytimg.com/vi/{identifier}/hqdefault.jpg`), estado (pausado/reproduciendo), duración (`FormatearDuracion`: `🔴 EN VIVO` / `h:mm:ss` / `m:ss`).

## 10. Descargas con yt-dlp — detalles
- Límite Discord: 9 MiB (9.437.184 bytes, holgado bajo 10 MB).
- Archivos grandes: embed con enlace litterbox (72h) + footer + tamaño en MB.
- URL validada con `Uri.TryCreate` (http/https).
- Errores yt-dlp muestran detalles solo en modo Debug (`Bot.Debug`).
- Los títulos de archivo se sanear (restrict-filenames), el título mostrado es el nombre de archivo.

## 11. Estado actual (agosto 2026)
**Funcionando:** todo lo de las secciones 7-8, Lavalink con youtube-source + LavaSrc, reproducción normal de YouTube, volumen persistente por servidor y enlaces de canciones individuales de Spotify mediante el fallback oEmbed → búsqueda YouTube del bot. Las playlists/álbumes de Spotify dependen de credenciales de Spotify porque el token anónimo está fallando actualmente. **Juego de conteo, chat Gemini (menciones + espontáneo) y notificaciones de YouTube** están implementados y cargados.

**Sesiones previas — resumen de lo estable:**
1. Añadido `Volume int?` a `GuildConfig`, migración `20260804062107_InitialCreate` y paso `EnsureCreatedAsync()` → `MigrateAsync()`.
2. `MusicService` inyecta `IDbContextFactory` para persistir/aplicar volumen.
3. LavaSrc 4.8.3 en `application.yml`; fallback oEmbed de Spotify.
4. `SPOTIFY_CLIENT_ID`/`SECRET` opcionales; `run.sh` carga `.env`.

**Validado:** el usuario confirmó que el volumen persistente, los enlaces de canciones de Spotify y el juego de conteo funcionan correctamente en Discord.

## 12. Pendientes / roadmap
- [x] Probar en Discord el volumen persistente y la reproducción de enlaces de canciones de Spotify.
- [x] Implementar el juego de conteo (bot de counting) completo.
- [x] IA: respuestas a menciones `@` y modo espontáneo con toggle por servidor.
- [x] Primera equivocación perdonada con pista privada por DM.
- [x] Notificaciones de YouTube vía feed RSS público (sin API key).
- [ ] Probar en Discord: modo espontáneo real (el umbral mínimo es ~101 mensajes; bajarlo a 2-5 temporalmente para validar rápido).
- [ ] Probar una suscripción de YouTube completa (suscribirse, esperar 5 min, subir vídeo o usar un canal activo).
- [ ] Añadir `SPOTIFY_CLIENT_ID` y `SPOTIFY_CLIENT_SECRET` al `.env` desde una aplicación de Spotify Developer si se quieren usar playlists/álbumes de Spotify sin depender del token anónimo.
- [ ] Fase 7: empaquetado VPS (systemd para el bot; Lavalink ya tiene run.sh, sin Docker). Decisión de despliegue: VPS Oracle Cloud con Ubuntu 24.04; IP pública activada; abrir solo 22/80/443; Lavalink en `127.0.0.1`; Portal web detrás de Caddy/Nginx con HTTPS.
- [ ] Migrar `Debug`/toggles/permisos por comando de appsettings/atributos a SQLite (que los admins lo configuren sin tocar código).
- [ ] Portal web para editar messages.json y permisos.
- [ ] Insignia Active Developer (endpoint HTTP de interacciones).

## 13. Comandos de operación (dev)
```bash
# Build
dotnet build src/Snowflake.Bot/Snowflake.Bot.csproj
# Migraciones
cd src/Snowflake.Bot && dotnet ef migrations add Nombre --context BotDbContext
# Arrancar bot (background)
nohup dotnet src/Snowflake.Bot/bin/Debug/net10.0/Snowflake.Bot.dll > /tmp/snowflake-bot.log 2>&1 &
# Arrancar Lavalink
nohup ./deploy/lavalink/run.sh > /tmp/lavalink.log 2>&1 &
# Parar (el truco [.] evita matar el propio shell)
pkill -f "Snowflake[.]Bot[.]dll"; pkill -f "Lavalink[.]jar"
# Test rápido del servidor Lavalink
curl -s -H "Authorization: youshallnotpass" http://127.0.0.1:2333/v4/loadtracks?identifier=YT_URL
```
- La BD está en `src/Snowflake.Bot/bin/Debug/net10.0/snowflake.db`.
- Logs: `/tmp/snowflake-bot.log` y `/tmp/lavalink.log`.
- El token está en `.env` (no subir a git; `.env.example` es la plantilla).

## 14. Registro de cambios — 2026-08-04

### Solicitud
- Recordar el volumen configurado en cada servidor y conservarlo después de reiniciar el bot.
- Hacer funcionar los enlaces de Spotify en `/m play`.

### Cambios realizados
1. Se añadió `Volume int?` a `GuildConfig`, persistido en la tabla `GuildConfigs` de SQLite.
2. Se reemplazó `EnsureCreatedAsync()` por `MigrateAsync()` y se generó la migración `20260804062107_InitialCreate`.
3. `/m volumen` ahora guarda el nivel por `GuildId` incluso cuando no existe un reproductor activo.
4. Al crear un reproductor nuevo, `MusicService` lee el volumen guardado y lo aplica a Lavalink.
5. Se corrigió el rango del volumen a `0–100`, incluyendo la protección contra desbordamiento de valores grandes del comando slash.
6. Se detectó que LavaSrc 4.8.3 falla al obtener el token anónimo de Spotify (`Failed to retrieve secret from Spotify`).
7. Para enlaces de canciones individuales, se añadió un fallback: Spotify oEmbed público → título de la canción → búsqueda en YouTube → reproducción mediante Lavalink.
8. Se añadieron las variables opcionales `SPOTIFY_CLIENT_ID` y `SPOTIFY_CLIENT_SECRET` a la configuración de Lavalink para playlists y álbumes.
9. `deploy/lavalink/run.sh` carga automáticamente el `.env` del proyecto antes de iniciar Lavalink, sin guardar secretos en el código.

### Validación
- La compilación de `Snowflake.Bot` terminó correctamente y sin advertencias.
- La migración creó `snowflake.db` con la columna `Volume`.
- El bot conectó correctamente con Discord.
- Lavalink conectó correctamente con el bot.
- `youtube-source` y `lavasrc-plugin` se cargaron correctamente.
- Spotify oEmbed respondió y Lavalink encontró la canción equivalente en YouTube.
- El usuario confirmó que la solución funciona en Discord.

### Archivos principales modificados
- `src/Snowflake.Bot/Services/MusicService.cs`
- `src/Snowflake.Bot/Modules/MusicModule.cs`
- `src/Snowflake.Bot/Program.cs`
- `deploy/lavalink/application.yml`
- `deploy/lavalink/run.sh`
- `CONTEXTO.md`

---

## 15. Registro de cambios — 2026-08-05 (sesión de features nuevas)

### Solicitudes
1. Bot de counting con comandos para elegir canal, cambiar modo de juego (binario/hexa/etc.), leaderboard, oportunidades extra diarias regenerativas (0-10), emojis de reacción configurables, mensaje de pérdida configurable, desenlazar canal, objetivo del servidor, y estadísticas por usuario.
2. Perdonar la **primera equivocación** de un usuario nuevo y enviarle por DM el número correcto (solo lo ve él).
3. IA Gemini: responder cuando lo mencionan con `@` (respondiendo a quien lo etiquetó), con toggle conmutable por servidor.
4. IA Gemini: cháchara espontánea en el chat sin mención, aleatoria tras ~100 mensajes (espera 1-50 más), no en canales muertos.
5. Notificaciones gratuitas de YouTube cuando un canal sube un vídeo, con mensaje personalizable y placeholders (cuándo se subió, etc.).

### Cambios realizados

#### A. Juego de conteo (feature completa)
- Nuevas entidades `CountingConfig` (config por servidor, PK `GuildId`, enum `CountingBase` guardado como string) y `CountingStat` (stats por usuario, único `(GuildId, UserId)`).
- `BotDbContext` con `DbSet`s e índices (`(GuildId,TargetUserId)` etc.).
- Sección `Conteo` en `messages.json` (placeholders `{cuenta}` `{usuario}` `{siguiente}` `{base}` `{objetivo}` `{nivel}` `{correcto}` `{incorrecto}` `{record}` `{vista}` `{total}` `{n}`).
- `CountingService` (clase `partial` por `[GeneratedRegex]`): conversión de bases con `Convert.ToString(value, radix)`/`Convert.ToInt64`, semáforo por guild contra carreras, detección de correcto/incorrecto, récord (celebrado una vez por cadena si supera el histórico), oportunidades extra diarias reseteadas en UTC (`LastExtraChanceResetDate` "yyyy-MM-dd"), validación de emojis (unicode + `:nombre:id` con `DiscordEmoji.FromGuildEmote`), leaderboard top 10 con medallas y stats (precisión, mejor aporte en la base activa).
- `CountingModule` con 9 subcomandos: `canal`, `desactivar`, `base`, `oportunidades`, `objetivo`/`objetivo-quitar`, `iconos`, `mensaje-perdida`, `leaderboard`, `estadisticas`. Todos los de config requieren `ManageGuild`.
- Hook `MessageCreated` en `DiscordBotService` (handler `OnMessageCreatedCounting`).
- Migración `20260805030536_AddCounting`.

#### B. Perdón de primera vez + hint privado
- En `CountingService.IncorrectoAsync`: si `stat.TotalCounts == 0 && stat.IncorrectCounts == 0` se reacciona 🛡️, **no se reinicia la cadena**, y se envía la pista por DM (`texto = "Conteo:PrimeraVezHint"` con `{siguiente}` = `CurrentValue+1` en la base activa).
- `EnviarHintPrivadoAsync`: obtiene el `DiscordMember` (cast o `guild.GetMemberAsync`), `CreateDmChannelAsync()` y envía. Fallback a `message.RespondAsync` si los MD están cerrados.
- Nueva clave `Conteo:PrimeraVezHint`.

#### C. Respuestas a menciones `@` (Gemini, toggle)
- Columna `GuildConfigs.GeminiMentionsEnabled bool` + migración `20260805051046_AddGeminiMentions`.
- `DiscordBotService.OnMessageCreated` refactored: detecta `MencionaAlBot` (por texto `<@id>`/`<@!id>`), lee el flag de la BD, limpia la mención (`LimpiarMencion`) y responde con Gemini (helper `ResponderChatAsync`).
- Comando `/gemini-menciones` en `ChatModule` con `Choice` on/off, permiso `ManageGuild`, valida `GEMINI_API_KEY` antes de activar. Sin argumento muestra el estado actual.
- Claves `Chat:MencionesActivadas/Desactivadas/FaltaApiKey`.

#### D. Modo espontáneo (Gemini, toggle)
- Columna `GuildConfigs.GeminiSpontaneousEnabled bool` + migración `20260805052114_AddGeminiSpontaneous`.
- `GeminiService`: caché en memoria `_espontaneoHabilitado`, estado por guild `EstadoEspontaneo` (umbral `100 + rand(1..50)`, cola de últimos 15 mensajes ambientales). `RegistrarMensajeParaEspontaneo` incrementa y dispara; `GenerarComentarioEspontaneoAsync` llama a Gemini con un prompt casual **sin tocar la conversación de `/charlar`**. Refactor `LlamarAGeminiAsync` compartido.
- `DiscordBotService`: `OnGuildDownloadCompleted` precarga los flags en caché; `OnMessageCreated` path 3 ignora comandos `/` y dispara `DispararComentarioEspontaneoAsync` en background (fire-and-forget).
- Comando `/gemini-espontaneo` (mismo patrón que el de menciones).
- Claves `Chat:EspontaneoActivado/Desactivado/FaltaApiKey`.

#### E. Notificaciones de YouTube (feed RSS, sin API key)
- Nueva entidad `YouTubeSubscription` (PK `GuildId`, 1 por server; índice en `YTChannelId` para agrupar fetches) + migración `20260805053351_AddYouTubeSubscriptions`.
- `YouTubeNotifyService` (`BackgroundService`): polling cada 5 min de `https://www.youtube.com/feeds/videos.xml?channel_id={UC}` (namespace `yt:videoId`), grupado por canal, **backfill silencioso** (al suscribir marca el último vídeo como `LastVideoId` y no notifica antiguos), resolución de URL/`@handle` con `yt-dlp --print channel_id`/`channel` (timeout 20s, `Process.Kill(entireProcessTree)`).
- Notificación: texto personalizado o por defecto + embed (título, autor, miniatura `i.ytimg.com/vi/{id}/hqdefault.jpg`, timestamp) + mención de rol opcional con `RoleMention(DiscordRole)` para que el ping funcione. Manejo de errores por canal aislado.
- `YouTubeModule` con `/youtube seguir/dejar/ver/rol/canal/mensaje`. Plantilla personalizable con placeholders `{canal}` `{titulo}` `{autor}` `{url}` `{videoId}` `{subido}` (ISO 8601) `{subidoREL}` ("hace 2 minutos"). El enlace se añade siempre después del texto si no estaba ya.
- `Program.cs`: `AddHttpClient("YouTube")` (15s), `AddSingleton<YouTubeNotifyService>()` + `AddHostedService<YouTubeNotifyService>()`.
- Sección `YouTube` en `messages.json` (24 claves).

### Validación
- La compilación queda limpia (0 errores/advertencias) tras cada feature.
- Se aplicaron las migraciones `AddCounting`, `AddGeminiMentions`, `AddGeminiSpontaneous`, `AddYouTubeSubscriptions` al arrancar el bot.
- `YouTubeNotifyService` arranca y loguea "polling cada 5 minutos".
- El usuario confirmó que `/counting` funciona tras darle a Snowflake permisos `ManageGuild`/Administrador (los `[SlashRequirePermissions]` validan también que el **bot** tenga el permiso, no solo el usuario — gotcha de DSharpPlus).
- El bot quedó corriendo con Lavalink enlazado.

### Gotchas encontrados esta sesión
- `[SlashRequirePermissions(X)]` en DSharpPlus **comprueba también que el bot tenga X** (no solo el usuario). Si el bot no tiene `ManageGuild`, el comando falla aunque el usuario sea el owner. Solución: dar Administrador al rol del bot, o usar `[SlashRequireUserPermissions]` si solo se quiere validar al usuario.
- `[GeneratedRegex]` exige que la clase contenedora sea `partial`. Aplicado a `CountingService`.
- `DiscordUser.CreateDmChannelAsync` no existe; hay que usar `DiscordMember.CreateDmChannelAsync` (cast o `guild.GetMemberAsync`).
- `DiscordMessage.MentionUsers` no existe en DSharpPlus 5; la detección de mención se hace por contenido (`<@id>`/`<@!id>`).
- Tras crear una migración con `dotnet ef migrations add` hay que **rebuild** antes de ejecutar la DLL o EF Core 10 lanza `PendingModelChangesWarning`.

### Archivos principales modificados/creados
- `src/Snowflake.Bot/Data/Entities/CountingConfig.cs` **(nuevo)**
- `src/Snowflake.Bot/Data/Entities/YouTubeSubscription.cs` **(nuevo)**
- `src/Snowflake.Bot/Services/CountingService.cs` **(nuevo)**
- `src/Snowflake.Bot/Services/YouTubeNotifyService.cs` **(nuevo)**
- `src/Snowflake.Bot/Modules/CountingModule.cs` **(nuevo)**
- `src/Snowflake.Bot/Modules/YouTubeModule.cs` **(nuevo)**
- `src/Snowflake.Bot/Services/GeminiService.cs` (modo espontáneo + LlamarAGeminiAsync)
- `src/Snowflake.Bot/Modules/ChatModule.cs` (`/gemini-menciones`, `/gemini-espontaneo`)
- `src/Snowflake.Bot/Services/DiscordBotService.cs` (MessageCreated 3 caminos, precarga espontáneo)
- `src/Snowflake.Bot/Data/BotDbContext.cs` (3 DbSet + entidades)
- `src/Snowflake.Bot/Data/Entities/GuildConfig.cs` (`GeminiMentionsEnabled`, `GeminiSpontaneousEnabled`)
- `src/Snowflake.Bot/Program.cs` (HttpClient("YouTube") + AddHostedService + AddSingleton)
- `src/Snowflake.Bot/messages.json` (secciones Conteo, YouTube; claves Chat nuevas)
- `CONTEXTO.md`
