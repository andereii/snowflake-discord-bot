# 📋 Dossier completo: Snowflake — Bot de Discord

> Documento de contexto para compartir el estado completo del proyecto con otra IA o desarrollador.
> Fecha: agosto 2026.
>
> ⚠️ **REGLAS DEL PROYECTO** (ver también `AGENTS.md`): el bot es trilingüe (en/es/pt, inglés por defecto). Todo mensaje nuevo va en los 3 `messages.*.json` con la misma clave, y todo comando slash lleva name/description localizados (es/pt). El idioma por servidor se cambia con `/lang` o desde el panel web.

## 1. Resumen
Bot de Discord en **C# / .NET 10** con **DSharpPlus 5.0.0** que ofrece: moderación documentada, bienvenidas, descarga de vídeos/audio, paletas de colores autoasignables, creación de canales, música vía Lavalink, sistema join-to-create de canales de voz, **juego de conteo** (con bases alternativas, récords, oportunidades extra y leaderboard), **chatbot con DeepSeek** (deepseek-v4-flash; vía `/talk`, mención `@` y modo espontáneo) y **notificaciones de YouTube** vía feed RSS público. Incluye además una **API REST (panel web de configuración)** alojada en el mismo proceso (secciones 16-17). El bot está **desplegado y corriendo en Fly.io**; el frontend del panel lo está desarrollando un colaborador aparte.

- **Nombre:** Snowflake (`Snowflake#3104`)
- **Client ID:** `1052318909035970641`
- **Guild de pruebas:** `1475204967567589440` (los comandos se registran SOLO aquí, para que aparezcan al instante)
- **Owner ID:** `553023040489914369`
- **Intents:** Guilds, GuildMembers, GuildBans, GuildVoiceStates, GuildMessages, **MessageContents** (necesario para conteo, menciones y cháchara espontánea)
- **Endpoint de interacciones:** vacío (funciona por gateway, no por HTTP)
- **Producción:** app Fly.io `snowflake-discord-bot-floral-river-8992` + app auxiliar `lavalink-silent-snowflake-2883`; API pública en `https://snowflake-discord-bot-floral-river-8992.fly.dev`

## 2. Stack y versiones
| Componente | Versión |
|---|---|
| .NET SDK (Sdk.Web) | 10.0.110 (net10.0) |
| DSharpPlus + DSharpPlus.SlashCommands | 5.0.0 estable |
| Lavalink4NET.DSharpPlus | 4.2.2 |
| EF Core Sqlite + Design | 10.0.10 |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.12 (parchea aviso de vulnerabilidad) |
| DotNetEnv | 3.2.0 |
| Microsoft.Extensions.Hosting | 10.0.10 |
| Lavalink (servidor Java) | 4.2.2 (jar en deploy/lavalink/, Java 17+) |
| yt-dlp | 2026.07.04 (instalado en el sistema) |
| ffmpeg | 8.1.2 (requerido por yt-dlp para audio) |

**Herramientas:** `dotnet-ef` global 10.0.11, `Microsoft.EntityFrameworkCore.Design` (paquete), Fly CLI (`~/.fly/bin/fly`).
**Máquina:** CachyOS (Arch). Docker NO instalado (la build de Fly.io ocurre en sus builders).
**Runtime ASP.NET:** al ser `Sdk.Web`, el binario necesita `Microsoft.AspNetCore.App` — instalado localmente (`aspnet-runtime-10.0`) e incluido en la imagen de Fly (`mcr.microsoft.com/dotnet/aspnet:10.0`).

## 3. Estructura del proyecto
```
snowflake_discord_bot/
├── .env / .env.example      # DISCORD_TOKEN (obligatorio), YT_COOKIES_FILE, GEMINI_API_KEY, WEB_PANEL_API_KEY (opcional), SPOTIFY_CLIENT_ID/SECRET (opcional)
├── Dockerfile, fly.toml     # despliegue Fly.io (bot + API web + Lavalink + volumen DATA_DIR)
├── deploy/lavalink/
│   ├── Lavalink.jar         # servidor 4.2.2
│   ├── application.yml      # config + plugins (youtube-source + lavasrc)
│   └── run.sh               # java -jar Lavalink.jar (carga .env antes)
└── src/Snowflake.Bot/
    ├── Snowflake.Bot.csproj # Sdk.Web (aloja bot + API REST del panel)
    ├── Program.cs           # host, DI, arranque (extensiones AddSnowflake*)
    ├── appsettings.json     # Bot:{TestGuildId,OwnerId,Debug,SettingsCacheSeconds}, Lavalink, Gemini, Database, YouTube, Music, Downloads, Colors
    ├── messages.en/es/pt.json  # TODOS los textos del bot en 3 idiomas (recarga en caliente; inglés base)
    ├── Configuration/       # BotConfiguration, GeminiOptions, LavalinkOptions, DatabaseOptions, YouTubeOptions, MusicOptions, DownloadOptions, ColorOptions
    ├── Endpoints/           # ConfigEndpoints (ajustes por sección), BotInfoEndpoints (servidores compartidos), ApiKeyGuard (X-Api-Key opcional)
    ├── Data/
    │   ├── BotDbContext.cs
    │   ├── BotDbContextFactory.cs  # IDesignTimeDbContextFactory para EF Migrations
    │   ├── Entities/        # Incident, GuildConfig, ColorRole, TempChannel, CountingConfig, CountingStat, YouTubeSubscription, ChannelLock
    │   └── Migrations/      # InitialCreate, AddCounting, AddGeminiMentions, AddGeminiSpontaneous, AddYouTubeSubscriptions, AddGuildFeatureToggles
    ├── Modules/             # comandos slash (SnowflakeModuleBase + 11 módulos)
    ├── Services/            # lógica de negocio (13 servicios + Settings/GuildSettingsService)
    └── Utilities/           # DurationParser, ChatResponseFormatter, BotEmojis
```

**Git:** el proyecto SÍ tiene repositorio propio en `/home/alex/Documentos/snowflake_discord_bot` (rama `main`, ~13 commits; incluye commits de sesiones posteriores como el despliegue Fly.io — antes de editar un archivo conviene `git show HEAD:ruta` para no pisar trabajo previo).

## 4. Arquitectura de arranque (`Program.cs`)
1. `Env.TraversePath().Load()` — carga `.env` (no sobreescribe vars del sistema).
2. **`WebApplication.CreateBuilder`** (Sdk.Web) con `ContentRootPath = AppContext.BaseDirectory`: el mismo proceso aloja el bot de Discord y la API REST del panel.
3. Registros modularizados con extensiones (clase `SnowflakeServiceExtensions` al final de `Program.cs`):
   - `AddSnowflakeOptions` → `IOptionsMonitor` de `BotConfiguration`, `GeminiOptions`, `ColorOptions`, `DatabaseOptions`, `YouTubeOptions`, `MusicOptions`, `DownloadOptions` (hot-reload de `Debug` incluido).
   - `AddSnowflakeDatabase` → `IDbContextFactory<BotDbContext>` SQLite; la ruta la decide `DatabaseOptions.ResolveFullPath()` (env `DATA_DIR` del volumen de Fly.io, si no junto al ejecutable). Suprime `PendingModelChangesWarning`.
   - `AddSnowflakeHttpClients` → `"Spotify"` (15s), `"Gemini"` (60s), `"YouTube"` (15s), `"Litterbox"` (10 min).
   - `AddSnowflakeServices` → `GuildSettingsService` (ajustes por servidor, caché TTL) + 12 servicios singleton + `AddHostedService<YouTubeNotifyService>`.
   - `AddDiscordClient` → `AddLavalink()` + `ConfigureLavalink` (`http://{Host}:{Port}`, passphrase, label `snowflake`), `DiscordClient` singleton con los intents (incluido `MessageContents`; lanza excepción clara si falta `DISCORD_TOKEN`) y `AddHostedService<DiscordBotService>`.
   - `AddSnowflakeCors` → política `SnowflakeWeb` (orígenes en `Web:AllowedOrigins`, `"*"` por defecto).
4. `messages.json` → `IConfiguration` con `reloadOnChange: true` + `MessagesService`.
5. Tras `Build()`: `app.UseCors(...)`, `db.Database.MigrateAsync()` (EF Migrations) y los endpoints: `GET /api/status`, `MapConfigEndpoints()`, `MapBotInfoEndpoints()`.
6. `DiscordBotService` registra los módulos slash con `UseSlashCommands` y `RegisterCommands(assembly, TestGuildId)`.

## 5. Base de datos SQLite (EF Core)
Tablas (migraciones aplicadas en orden: `InitialCreate`, `AddCounting`, `AddGeminiMentions`, `AddGeminiSpontaneous`, `AddYouTubeSubscriptions`, `AddGuildFeatureToggles`, `AddChannelLocks`):

**`Incidents`** — historial de moderación (número de caso autoincremental):
`Id`, `GuildId`, `TargetUserId`, `TargetTag`, `ModeratorId`, `ModeratorTag`, `Type` (enum guardado como string: Advertencia/Expulsion/Veto/Aislamiento/FinAislamiento), `Reason`, `Duration TimeSpan?` (solo aislamientos), `CreatedAt`. Índice `(GuildId, TargetUserId)`.

**`GuildConfigs`** — config por servidor (PK `GuildId`):
`ModLogChannelId ulong?`, `WelcomeChannelId ulong?`, `WelcomeMessage string?`, `HubChannelId ulong?`, `TempChannelNameTemplate string?` (plantilla de canales temporales, `{usuario}`), `Volume int?` (0-100, persistente, null = 100 por defecto), `DjRoleId ulong?` (rol DJ para controlar la música), `GeminiChatEnabled bool` (default true, interruptor de /talk), `GeminiMentionsEnabled bool` (toggle de respuestas a `@`), `GeminiSpontaneousEnabled bool` (toggle de cháchara espontánea), `AiWebSearchEnabled bool` (default true; búsqueda web de la IA a criterio del modelo, /ai-search o panel), `DownloadsEnabled bool` (default true, interruptor de /download), `Language string` (default "en"; "en"/"es"/"pt", vía /lang o panel web).

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

**`ChannelLocks`** — canales en lockdown (`/bloquear`):
`ChannelId` (PK), `GuildId`, `AllowBits long` / `DenyBits long` (overwrite original de @everyone), `HadOverwrite bool`, `LockedAt`. Índice `GuildId`.

**Gotcha conocido:** con `EnsureCreated` la BD no se actualiza al añadir columnas (hubo que borrarla a mano dos veces); por eso se pasó a EF Migrations. Tras crear una migración con `dotnet ef migrations add` hay que **reconstruir** (`dotnet build`) antes de ejecutar la DLL: si no, EF Core 10 lanza `PendingModelChangesWarning` porque el ensamblado no incluye el snapshot actualizado.

## 6. Sistema de textos (`messages.*.json` + `MessagesService`)
- **Tres archivos por idioma:** `messages.en.json` (base), `messages.es.json`, `messages.pt.json` — misma clave en los tres. `MessagesService.Get(guildId, "Clave", placeholders)` resuelve el idioma del servidor (caché) con fallback a inglés; `Get(locale, ...)` para idioma explícito; `Locale(guildId)` devuelve "en"/"es"/"pt".
- Estructura de secciones: `Ping`, `Errores`, `Presentacion`, `Moderacion`, `Descargas`, `Bienvenida`, `Musica`, `Colores`, `Voces`, `Config`, `Chat`, `Conteo`, `YouTube`, `Limpiar`, `Bloqueo`.
- Se accede por clave con `:` separando niveles: `msg.Get("Musica:NoEncontrado")`, con placeholders: `msg.Get("Bienvenida:MensajePorDefecto", ("usuario", mention), ("servidor", nombre))`.
- Placeholders en el archivo van entre llaves: `{usuario}`, `{servidor}`, `{motivo}`, `{titulo}`, `{duracion}`, `{nivel}`, etc.
- `reloadOnChange: true` → editar los JSON en caliente cambia los textos al instante, sin reiniciar. Los comandos slash se localizan en código con `[NameLocalization]`/`[DescriptionLocalization]` (Discord muestra la versión según el idioma del CLIENTE del usuario).
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
| `/descargar` `{url, formato: Vídeo\|Solo audio}` | todos (si `DownloadsEnabled`) | yt-dlp: <MaxDiscordBytes se adjunta, más grande → litterbox 72h |
| `/canal crear` `{nombre,tipo voz\|texto,categoria?}` | ManageChannels | Crea canal |
| `/canal hub` `{canal}` | ManageGuild | Activa join-to-create en ese canal de voz |
| `/canal hub-quitar` | ManageGuild | Lo desactiva |
| `/canal plantilla` `{plantilla?}` | ManageGuild | Nombre de canales temporales (`{usuario}`; vacío = por defecto) |
| `/config show` | todos (efímero) | Resumen de TODOS los ajustes del servidor (mini-panel) |
| `/lang` `{language?}` | ManageGuild | Idioma del bot en el servidor (English/Español/Português; vacío = mostrar actual) |
| `/clear` `{cantidad 1-100, canal?}` | ManageMessages (usuario y bot, verificado en el canal destino) | Borrado masivo <14 días + individual con pausa para viejos |
| `/bloquear` `{canal?, motivo?}` | ManageChannels (verificado en el canal destino) + bot ManageRoles | Lockdown: niega a @everyone enviar mensajes (texto) o conectarse (voz); guarda el overwrite original en `ChannelLocks` |
| `/desbloquear` `{canal?, motivo?}` | igual que /bloquear | Restaura EXACTAMENTE el overwrite que había antes del bloqueo |
| `/colores instalar` `{paleta: normal\|pastel}` | ManageRoles | Crea los 17 roles de color |
| `/colores desinstalar` | ManageRoles | Borra todos los roles de color |
| `/colores elegir` | todos | Menú de selección efímero (custom_id `snowflake_colores`, opción "Quitar color" valor `0`) |
| `/colores quitar` | todos | Te quita el color |
| `/colores listar` | todos | Lista colores instalados (efímero) |
| `/m play` `{consulta}` | todos | URL o búsqueda (YouTube/Spotify) → reproduce o encola + widget |
| `/m skip` | control | Salta, avisa de la siguiente o de cola vacía (mismo canal de voz / rol DJ / ManageGuild) |
| `/m cola` | todos | Embed: sonando ahora + siguientes + duración total |
| `/m pausa` `/m reanuda` `/m stop` | control | Control (mismo canal de voz / rol DJ / ManageGuild) |
| `/m volumen {nivel}` | todos | Volumen persistente por server (acotado por MusicOptions) |
| `/talk` `{text}` | todos (si `GeminiChatEnabled`) | Habla con DeepSeek (conversación compartida por server) |
| `/talk-clear` | ManageGuild | Reinicia la conversación compartida |
| `/ai-mentions` `{state on/off?}` | ManageGuild | Toggle: ¿responder al ser mencionado con @? |
| `/ai-spontaneous` `{state on/off?}` | ManageGuild | Toggle: ¿intervenir solo en el chat cada ~100+rand(1..50) mensajes? |
| `/ai-search` `{state on/off?}` | ManageGuild | Toggle: búsqueda en internet de la IA (web_search de DeepSeek; el modelo decide cuándo usarla) |
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

**Toggles de IA:** requieren `DEEPSEEK_API_KEY` en `.env`. Si falta, no deja activar.

**Reglas de moderación (importantes):** no auto-acciones, no al bot, no al owner, jerarquía de roles (`miembro.Hierarchy >= bot.Hierarchy` → error), DM de aviso best-effort (si tiene MD cerrados sigue), cada acción crea un `Incident` y se anuncia en el canal de logs con embed de color por tipo + número de caso + timestamp.

## 8. Servicios y eventos

**`DiscordBotService`** (BackgroundService, el corazón):
- `Ready`, `GuildDownloadCompleted` (logs + **precarga de flags espontáneo en caché**), `GuildCreated` → presenta al bot en el system channel o primer canal de texto donde pueda escribir.
- `GuildMemberAdded` → bienvenida (usa config de BD o mensaje por defecto; ignoran bots).
- `VoiceStateUpdated` → delega en `VoiceHubService`.
- `ComponentInteractionCreated` → **router de componentes**: `snowflake_colores` → ColorService, botones del widget de música (`MusicWidgetService.EsInteraccionMusica`) → MusicWidgetService.
- `MessageCreated` → tres caminos: (1) respuesta a un mensaje de Gemini generado por `/charlar` → continúa la conversación; (2) **mención `@` al bot** si `GeminiMentionsEnabled` → responde con Gemini; (3) **cháchara espontánea** si `GeminiSpontaneousEnabled` → cuenta el mensaje y dispara un comentario por el canal al alcanzar el umbral.
- `SlashCommandErrored` → si es `SlashExecutionChecksFailedException` → "SinPermisos"; si no, mensaje de error con detalles solo en modo `Debug`.

**`GuildSettingsService`** (Services/Settings, punto único de configuración): toda lectura/escritura de ajustes por servidor pasa por aquí (bot y panel web). `GuildConfig` cacheada en memoria con TTL (`Bot:SettingsCacheSeconds`); `CountingConfig`/`YouTubeSubscription` sin caché. Mutaciones: `UpdateAsync`/`UpdateCountingAsync`/`UpdateYouTubeAsync`/`DeleteYouTubeAsync` (invalidan la caché). `GetSnapshotAsync` devuelve `GuildSettingsSnapshot`, el contrato JSON del panel (IDs como string, secciones Moderación/Bienvenida/Voz/Música/IA/Descargas/Conteo/YouTube).

**`VoiceHubService`** (join-to-create): al entrar al canal hub → crea canal con el nombre de `TempChannelNameTemplate` (placeholder `{usuario}`; si no, el por defecto de messages.json) en la misma categoría, con overwrites al dueño (ManageChannels, MoveMembers, MuteMembers, DeafenMembers, AccessChannels, UseVoice), lo mueve dentro y lo registra en `TempChannels`. Al salir de un canal temporal → si queda vacío lo borra y elimina el registro. Ignora cambios de mute/deafen.

**`ColorService`**: dos paletas de 17 colores **configurables en appsettings.json** (sección `Colors`, vía `ColorOptions`): Normal: Rojo→Negro; Pastel: Rosa pastel→Perla. `InstalarAsync` reemplaza paletas cruzadas (borra roles de la otra paleta y crea los que falten). Roles con prefijo `• `. `HandleSelectAsync` gestiona el menú (quita el color anterior, asigna el nuevo).

**`MusicService`**: lógica Lavalink4NET (ver sección 9). El volumen persistente se lee/guarda vía `GuildSettingsService` y se acota con `MusicOptions` (MinVolume/MaxVolume).

**`MusicWidgetService`**: widget "reproduciendo ahora" (embed + 4 botones: ⏯️ pausa, ⏭️ skip, 📋 cola, ⏹️ stop; custom_ids `snowflake_music_pause/skip/cola/stop`). Un widget por guild, guardado en `ConcurrentDictionary<guildId, (messageId, channelId)>`. **No se autoactualiza** — solo se refresca ante acciones del usuario (decisión de diseño deliberada, sin barra de progreso viva). Stop deja el widget estático con botones deshabilitados y lo borra tras `MusicOptions.WidgetDeleteDelaySeconds` (default 5s). El botón cola responde efímero. `EsInteraccionMusica(customId)` es el check del router de componentes.

**`CountingService`** (juego de conteo): parsee en base configurada (`IntentarParsear`/`Formatear` con `Convert.ToString(value, radix)`), semáforo por guild contra carreras, persistencia de config/stats. Detecta: correcto (reacciona ✅/🎉 y actualiza récord), incorrecto normal (❌ + reset), **primera vez perdonada** (🛡️ + DM hint privado con el número correcto, fallback a reply si MD cerrados), oportunidades extra diarias (🛡️, reset diario UTC), mismo usuario dos veces (reset). Construye embeds de leaderboard (top 10 con medallas) y stats (totales, precisión, mejor aporte en la base activa).

**`DeepSeekService`** (chatbot): Responses API de DeepSeek (`POST https://api.deepseek.com/responses`; modelo por defecto `deepseek-v4-flash`, configurable con `DEEPSEEK_MODEL`). **Búsqueda web nativa:** envía `tools: [{type:"web_search"}]` con `tool_choice:"auto"` (el modelo decide cuándo buscar; la ejecuta el servidor de DeepSeek) si el servidor tiene `AiWebSearchEnabled`; los items de salida (`message` + `web_search_call`) se guardan en el historial compartido y se devuelven tal cual en el siguiente turno (la API restaura los resultados automáticamente). El modo espontáneo es una llamada puntual SIN búsqueda. Conversación compartida por servidor (histórico acotado por `MaxHistoryTurns`), serialización por `SemaphoreSlim` + límite de solicitudes simultáneas por guild (`MaxConcurrentPerGuild`, default 2). `_mensajesGenerados` (messageId→guildId) para detectar respuestas a mensajes del bot. `Limpiar` reinicia. **Modo espontáneo:** `EstadoEspontaneo` por guild con umbral `SpontaneousBaseMessages + jitter(min..max)` (defaults 100 y 1..50) y cola de últimos `SpontaneousRecentBuffer` (default 15) mensajes ambientales; `GenerarComentarioEspontaneoAsync` llama a Gemini con un prompt casual sin tocar la conversación de `/charlar`. Refactor `LlamarAGeminiAsync` compartido por `PreguntarAsync` y el espontáneo. Errores `GeminiException`/`GeminiBusyException`.

**`YouTubeNotifyService`** (BackgroundService): polling del feed RSS público `https://www.youtube.com/feeds/videos.xml?channel_id={UC}` (intervalo y retardos en `YouTubeOptions`; default 5 min; sin API key). Agrupa suscripciones por `YTChannelId` (un fetch por canal), parsea XML con `XDocument`, compara `LastVideoId`. **Backfill silencioso** al crear suscripción (marca el último vídeo como visto, no notifica antiguos). Resolución de URL/`@handle` a `channel_id` con `yt-dlp --print channel_id` (+`channel` para el nombre; timeouts en options). Notificación: texto (personalizado o por defecto) + embed con título, autor, miniatura `i.ytimg.com/vi/{id}/hqdefault.jpg`, timestamp, y mención de rol opcional (`RoleMention` para que el ping funcione). Errores por canal aislados (un feed caído no aborta el resto).

**`DownloadService`**: lanza `yt-dlp` como proceso externo con args `--no-playlist --no-progress --no-warnings --no-part --restrict-filenames --print after_move:filepath -o {plantilla}`, cookies opcionales de `YT_COOKIES_FILE`, modo audio = `-x --audio-format mp3 --audio-quality 0`. Timeout duro desde `DownloadOptions` (`TimeoutMinutes` + 1 min de margen), plantilla `%(title).80B [%(id)s].%(ext)s` en `/tmp/snowflake/{guid}/`. Errores → `YtDlpException` con las últimas 3 líneas de stderr saneadas (máx 800 chars). El llamador limpia el temp dir en `finally`.

**`LitterboxService`**: multipart POST a `litterbox.catbox.moe` con `reqtype=fileupload`, `time=72h` (cliente HTTP nombrado `"Litterbox"`). Devuelve la URL (validada).

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
- Límite para adjuntar en Discord: `DownloadOptions.MaxDiscordBytes` (default 9.437.184 bytes = 9 MiB, holgado bajo 10 MB); lo que lo supere va a litterbox.
- Archivos grandes: embed con enlace litterbox (72h) + footer + tamaño en MB.
- URL validada con `Uri.TryCreate` (http/https).
- Errores yt-dlp muestran detalles solo en modo Debug (`Bot.Debug`).
- Los títulos de archivo se sanear (restrict-filenames), el título mostrado es el nombre de archivo.
- Interruptor por servidor: `DownloadsEnabled` (default true).

## 11. Estado actual (agosto 2026)
**En producción (Fly.io):** el bot corre en la app `snowflake-discord-bot-floral-river-8992` (máquina `e822700fd17278`) con su BD en el volumen `/app/data/snowflake.db`; Lavalink en la app auxiliar `lavalink-silent-snowflake-2883` (hostname interno `lavalink-silent-snowflake-2883.internal:2333`). La API del panel responde en `https://snowflake-discord-bot-floral-river-8992.fly.dev/api/...`. **No debe haber otra instancia del bot con el mismo token** (local quedó detenido).

**Funcionando:** moderación, bienvenidas, descargas, colores, canales, join-to-create, música (volumen persistente + fallback Spotify oEmbed), juego de conteo, chat IA DeepSeek (menciones + espontáneo), notificaciones de YouTube, `/config ver` y la API REST del panel (snapshot + patches + servidores compartidos). Frontend del panel: **en desarrollo por un colaborador** (ya conectado el contrato de `/api/bot/shared-guilds`).

**Sesiones previas — resumen de lo estable:**
1. Añadido `Volume int?` a `GuildConfig`, migración `20260804062107_InitialCreate` y paso `EnsureCreatedAsync()` → `MigrateAsync()`.
2. Volumen persistente por servidor aplicado en Lavalink.
3. LavaSrc 4.8.3 en `application.yml`; fallback oEmbed de Spotify.
4. `SPOTIFY_CLIENT_ID`/`SECRET` opcionales; `run.sh` carga `.env`.
5. Fly.io: `Dockerfile` + `fly.toml` (bot + API web en `ASPNETCORE_URLS`, BD en volumen `DATA_DIR`, Lavalink como máquina auxiliar).
6. Refactorización completa (sección 16) y API de servidores compartidos + CORS (sección 17).

**Validado:** el usuario confirmó que el volumen persistente, los enlaces de canciones de Spotify y el juego de conteo funcionan correctamente en Discord. La API del panel quedó probada en producción (snapshot, POST de ajustes y shared-guilds).

## 12. Pendientes / roadmap
- [x] Probar en Discord el volumen persistente y la reproducción de enlaces de canciones de Spotify.
- [x] Implementar el juego de conteo (bot de counting) completo.
- [x] IA: respuestas a menciones `@` y modo espontáneo con toggle por servidor (ahora con DeepSeek).
- [x] Primera equivocación perdonada con pista privada por DM.
- [x] Notificaciones de YouTube vía feed RSS público (sin API key).
- [x] Refactorización: modularizar, quitar hardcodes, auditoría de comandos peligrosos y preparar configs para portal web (sección 16).
- [x] API REST del panel web (`/api/guilds/{id}/config`, patch por secciones, API key opcional). Referencia completa en la sección 18.
- [x] Endpoint `/api/bot/shared-guilds` + CORS para el frontend del panel (secciones 17-18).
- [ ] Probar en Discord: modo espontáneo real (el umbral mínimo es ~101 mensajes; bajarlo a 2-5 temporalmente para validar rápido).
- [ ] Probar una suscripción de YouTube completa (suscribirse, esperar 5 min, subir vídeo o usar un canal activo).
- [ ] Frontend del panel web (HTML/JS) consumiendo la API REST (en curso, a cargo de un colaborador); añadir OAuth de Discord antes de exponerla a Internet.
- [ ] Añadir `SPOTIFY_CLIENT_ID` y `SPOTIFY_CLIENT_SECRET` al `.env` desde una aplicación de Spotify Developer si se quieren usar playlists/álbumes de Spotify sin depender del token anónimo.
- [ ] Fase 7 (opcional): empaquetado VPS propio (systemd para el bot; Lavalink ya tiene run.sh, sin Docker). Decisión de despliegue anterior: VPS Oracle Cloud con Ubuntu 24.04; IP pública activada; abrir solo 22/80/443; Lavalink en `127.0.0.1`; Portal web detrás de Caddy/Nginx con HTTPS. (Hoy el bot ya vive en Fly.io.)
- [ ] Insignia Active Developer (endpoint HTTP de interacciones).

## 13. Comandos de operación (dev)
```bash
# Build local (necesita aspnet-runtime-10.0 para EJECUTAR; compilar no lo requiere)
dotnet build src/Snowflake.Bot/Snowflake.Bot.csproj
# Migraciones (dotnet-ef exige runtime ASP.NET instalado; ya está)
cd src/Snowflake.Bot && dotnet ef migrations add Nombre --context BotDbContext
# Despliegue a producción (la build ocurre en los builders de Fly)
fly deploy --ha=false          # CLI en ~/.fly/bin/fly
# Estado / logs / SSH del VPS
fly status; fly logs --no-tail
fly ssh console -C "..."       # BD de producción: /app/data/snowflake.db
# Arrancar bot EN LOCAL (solo si el de Fly está parado; nunca dos instancias)
nohup dotnet src/Snowflake.Bot/bin/Debug/net10.0/Snowflake.Bot.dll > /tmp/snowflake-bot.log 2>&1 &
# Arrancar Lavalink local (mismo aviso de instancia única)
nohup ./deploy/lavalink/run.sh > /tmp/lavalink.log 2>&1 &
# Parar (el truco [.] evita matar el propio shell)
pkill -f "Snowflake[.]Bot[.]dll"; pkill -f "Lavalink[.]jar"
# Test rápido del servidor Lavalink
curl -s -H "Authorization: youshallnotpass" http://127.0.0.1:2333/v4/loadtracks?identifier=YT_URL
# API del panel en producción
curl -s https://snowflake-discord-bot-floral-river-8992.fly.dev/api/status
```
- BD local (dev): `src/Snowflake.Bot/bin/Debug/net10.0/snowflake.db`. BD de producción: volumen Fly en `/app/data/snowflake.db`.
- Logs locales: `/tmp/snowflake-bot.log` y `/tmp/lavalink.log`; en producción: `fly logs`.
- El token está en `.env` (no subir a git; `.env.example` es la plantilla). Secretos de Fly: `fly secrets set NOMBRE=valor`.

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

---

## 16. Registro de cambios — 2026-08-12 (refactorización + auditoría + panel web)

### Solicitudes
1. Leer todo el código y refactorizar por completo: modularizar, que quede legible para intervenciones futuras y sin componentes hardcodeados.
2. Auditoría de TODOS los comandos destructivos/peligrosos: que nadie sin permiso pueda usarlos.
3. Preparar las configuraciones para un portal web, cubriendo todo lo configurable que suele tener un bot comercial.

### Cambios realizados

#### A. Capa de configuración global (Options pattern)
- Nuevos `Configuration/DatabaseOptions` (ruta BD, respeta `DATA_DIR` de Fly.io), `YouTubeOptions` (polling, retardos, timeouts de yt-dlp), `MusicOptions` (delay de borrado del widget, rango de volumen), `DownloadOptions` (MaxDiscordBytes, timeout) y `ColorOptions` (paletas).
- `GeminiOptions` extendida: `MaxConcurrentPerGuild`, `SpontaneousBaseMessages`, `SpontaneousJitterMin/Max`, `SpontaneousRecentBuffer` (antes hardcodeados 2 / 100+1..50 / 15).
- `BotConfiguration` añade `SettingsCacheSeconds` (TTL de la caché de ajustes).

#### B. GuildSettingsService — punto único de configuración (web-ready)
- `Services/Settings/GuildSettingsService`: toda lectura/escritura de ajustes pasa por aquí (bot hoy, panel web mañana). `GuildConfig` cacheada con TTL (ruta caliente del chat); `CountingConfig`/`YouTubeSubscription` sin caché (cambian a menudo). Mutaciones vía `UpdateAsync`/`UpdateCountingAsync`/`UpdateYouTubeAsync`/`DeleteYouTubeAsync` con invalidación de caché.
- `Services/Settings/GuildSettingsSnapshot`: contrato JSON del panel (IDs como string para JavaScript; secciones Moderación/Bienvenida/Voz/Música/IA/Descargas/Conteo/YouTube).

#### C. Endpoints REST del panel web
- `Endpoints/ConfigEndpoints`: `GET /api/guilds/{id}/config` (snapshot completo), `POST /` (patch de GuildConfig, null = no tocar), `POST /counting`, `POST|DELETE /youtube`. Todo a través de `GuildSettingsService` (nunca dos caminos de escritura).
- **Seguridad:** si se define `WEB_PANEL_API_KEY` en `.env`, las mutaciones exigen cabecera `X-Api-Key`. Pendiente de producción: OAuth de Discord + HTTPS antes de exponer a Internet (documentado en el propio archivo).
- `Program.cs` modularizado con extensiones `AddSnowflakeOptions/Database/HttpClients/Services/DiscordClient`; mantiene `WebApplication` (Sdk.Web), `/api/status`, `DATA_DIR` y la supresión de `PendingModelChangesWarning`.

#### D. Auditoría de comandos peligrosos (endurecido)
- `/charlar-limpiar`: de "todos" a `ManageGuild` (cualquiera podía borrar la conversación compartida de IA).
- `/clear`: ahora verifica los permisos `ManageMessages` de **usuario y bot en el canal destino** (los overrides de canal pueden quitarlos aunque el permiso global exista). Nuevas claves `Limpiar:SinPermisosCanal/SinPermisosBotCanal`.
- Música: `skip/pausa/reanuda/stop` exigen estar en el mismo canal de voz que el bot, tener el **rol DJ** (`DjRoleId`, nuevo ajuste) o `ManageGuild`. `/m play` sigue abierto (como en bots comerciales). Nuevas claves `Musica:MismoCanal/RequiereDj`.
- `/descargar` y `/charlar`: interruptores por servidor `DownloadsEnabled`/`GeminiChatEnabled` (default true). Nuevas claves `Descargas:Desactivado`, `Chat:Desactivado`.
- Moderación ya estaba bien protegida (Kick/Ban/Moderate + permisos del bot + jerarquía de roles + no al owner/al bot/a sí mismo). Se mantiene.

#### E. Deshardcoding general
- `GeminiService`: concurrencia y parámetros del espontáneo desde options.
- `YouTubeNotifyService`: polling/retardos/timeouts desde `YouTubeOptions`; `ResolverCanalAsync` pasa de static (recibía ILogger) a método de instancia.
- `MusicWidgetService`: delay de borrado desde `MusicOptions`; nuevo `EsInteraccionMusica(customId)` público (se elimina el literal `"snowflake_music_"` del router).
- `DownloadService`: tope duro desde `DownloadOptions` (TimeoutMinutes + 1 de margen).
- `MusicService`: clamp de volumen desde `MusicOptions`; volumen persistente vía `GuildSettingsService`.
- `LitterboxService`: pasa de `static HttpClient` a `IHttpClientFactory("Litterbox")`.
- `VoiceHubService`: plantilla de nombre desde `TempChannelNameTemplate` (nuevo comando `/canal plantilla`).
- `DiscordBotService`: sin `IDbContextFactory` (usa settings service); router con constantes de MusicWidgetService.
- `CountingService`: emojis de reacción por defecto como constantes nombradas (`EmojiCorrectoPorDefecto`…).
- `Utilities/BotEmojis`: constantes de los emojis de aplicación (check/error/load/loadingwindows/snowflake) para mensajes compuestos en código.
- `Modules/SnowflakeModuleBase`: base común de módulos (ResponderAsync texto/embed, ResponderErrorAsync efímero con emoji de error, SafeEditAsync) — elimina la duplicación en 10 módulos.
- `ModerationModule`: el `❌` hardcodeado pasa a `BotEmojis.Error`.
- `CountingModule`: quita la segunda consulta redundante de `/counting objetivo`.

#### F. Migración y datos
- Nuevos campos en `GuildConfigs`: `DjRoleId`, `TempChannelNameTemplate`, `GeminiChatEnabled` (default true), `DownloadsEnabled` (default true).
- Migración `20260812000000_AddGuildFeatureToggles` (escrita a mano: el tool `dotnet-ef` no corre sin runtime ASP.NET local) y aplicada a la BD local.
- Nuevas claves en `messages.json` (27): Musica, Limpiar, Chat, Descargas, Voces, Config (resumen `/config ver`).
- Nuevo comando `/config ver`: resumen efímero de todos los ajustes (mini-panel en Discord).

### Validación
- Compilación limpia (0 errores, 0 advertencias) con `Sdk.Web`.
- Migración aplicada a la BD local y marcada como aplicada en la BD del volumen de Fly.io (las columnas ya existían allí: `InitialCreate` del VPS era el actualizado; se insertó la fila en `__EFMigrationsHistory` para evitar un ALTER duplicado).
- **Desplegado en Fly.io** (`fly deploy`, máquina `e822700fd17278`): bot conectado al gateway de Discord (TCP establecido a :443), Lavalink reanudado (`lavalink-silent-snowflake-2883`, conexión establecida a :2333), API pública respondiendo en `https://snowflake-discord-bot-floral-river-8992.fly.dev/api/status` y ciclo POST de ajustes validado en local (volumen 2→25→2).
- El bot local y el Lavalink local quedaron DETENIDOS: la única instancia activa es la de Fly.io (el usuario quiere preservar los datos del volumen del VPS).

### Gotchas nuevos esta sesión
- El proyecto pasó a `Microsoft.NET.Sdk.Web`: el binario ya no arranca sin el runtime ASP.NET (`Microsoft.AspNetCore.App`). En Fly.io sí está (imagen `mcr.microsoft.com/dotnet/aspnet:10.0`); en local hay que instalar `aspnet-runtime-10.0`.
- `dotnet ef` dejó de funcionar en local por lo mismo (necesita el runtime ASP.NET): las migraciones nuevas se escriben a mano siguiendo el patrón existente hasta instalar el runtime.
- El git de este repo ya contenía commits de sesiones posteriores (Fly.io, refactor web inicial): antes de editar un archivo conviene comparar con HEAD (`git show HEAD:ruta`) para no pisar trabajo previo.
- messages.json tiene indentación mixta y comentarios: las ediciones automáticas deben preservar los comentarios (el parser de .NET los tolera) y vigilar la coma de la última clave de cada sección.
- En Fly.io los nombres de host de otras apps se resuelven como `{app}.internal` (p. ej. `lavalink-silent-snowflake-2883.internal`); si la app auxiliar está suspendida, el bot la reintenta cada minuto y falla con "Name or service not known" hasta reanudarla (`fly machine start <id> -a <app>`).
- Dos instancias del bot con el mismo token NO deben convivir (gateway conflictivo): en local o en el VPS, nunca en ambos a la vez.

---

## 17. Registro de cambios — 2026-08-13 (API de servidores compartidos + CORS para el panel web)

### Solicitud
El compañero que desarrolla el frontend del panel necesita un endpoint que resuelva "en cuáles de los servidores del usuario está el bot": desde el navegador (solo con el token del usuario) Discord no lo dice. Su mock `filtrarServidoresDelBot` quedará conectado a esta API.

### Cambios realizados
- **`Endpoints/BotInfoEndpoints.cs` (nuevo):** `POST /api/bot/shared-guilds` — el panel envía `{ "guildIds": ["id1", …] }` (strings, por la precisión de JS) y el bot responde `{ "shared": [ { "id": "…", "name": "…" }, … ] }` con los servidores del usuario donde el bot está presente (fuente: `DiscordClient.Guilds`, el caché del gateway). Si aún no conectó, devuelve lista vacía.
- **`Endpoints/ApiKeyGuard.cs` (nuevo):** guarda compartida de `X-Api-Key` (env `WEB_PANEL_API_KEY` opcional); `ConfigEndpoints` ahora la reutiliza en vez de tener la suya.
- **CORS en `Program.cs`:** política `SnowflakeWeb` (`Web:AllowedOrigins` en appsettings.json, `"*"` por defecto; en producción, la URL del frontend). Sin esto el navegador bloquearía las llamadas al API.
- Sección `Web` nueva en `appsettings.json`.

### Contrato para el frontend
```
POST {baseUrl}/api/bot/shared-guilds
Content-Type: application/json
{ "guildIds": ["1475204967567589440", …] }   // los que el usuario administra (token OAuth del usuario)
→ 200 { "shared": [ { "id": "1475204967567589440", "name": "El servidor de Britex" } ] }
```
- `{baseUrl}` en producción: `https://snowflake-discord-bot-floral-river-8992.fly.dev`.
- Si se configura `WEB_PANEL_API_KEY`, añadir cabecera `X-Api-Key`.
- El resto del API del panel está en `ConfigEndpoints` (sección 16) — referencia completa en la sección 18.

### Validación
- Desplegado en Fly.io y probado en producción: con 3 IDs (2 reales + 1 falso) devuelve exactamente los 2 servidores del bot, con nombres.
- Cabecera `access-control-allow-origin: *` verificada con `Origin` simulado.
- Bot conectado tras el redeploy (gateway Discord :443 y Lavalink :2333 establecidos).

---

## 18. Referencia de la API REST (panel web)

> Contrato completo para el frontend. Base en producción: `https://snowflake-discord-bot-floral-river-8992.fly.dev` (local: `http://localhost:5000`).

### Convenciones generales
- **Autenticación:** si el backend tiene definida la variable `WEB_PANEL_API_KEY`, TODA mutación (POST/DELETE) exige la cabecera `X-Api-Key: <clave>`. Si la clave no está definida, las mutaciones pasan sin ella (desarrollo). Respuesta de fallo: `401 Unauthorized`.
- **CORS:** política `SnowflakeWeb` — orígenes permitidos en `appsettings.json` → `Web:AllowedOrigins` (default `["*"]`).
- **IDs de Discord:** siempre como **string** en JSON (JavaScript pierde precisión con enteros > 2^53). El backend los parsea con `ulong.TryParse` (los que no parsean se ignoran).
- **Formato:** JSON; la serialización usa camelCase (p. ej. `djRoleId`, `youTube`).
- **Patching:** en los POST, un campo `null` significa "no tocar este ajuste". Para "quitar" un valor (canal, rol, plantilla…) se envía `""` (string vacía), que se interpreta como null.

### Endpoints

#### `GET /api/status`
Health check. No requiere auth.
```json
{ "status": "online", "timestamp": "2026-08-13T00:44:55.828Z" }
```

#### `POST /api/bot/shared-guilds` — ¿en cuáles de mis servidores está el bot?
El navegador solo conoce los servidores del USUARIO; este endpoint cruza esa lista con los servidores del bot.
```json
// Request
{ "guildIds": ["1475204967567589440", "…"] }
// Response 200
{ "shared": [ { "id": "1475204967567589440", "name": "El servidor de Britex" } ] }
```
Si el bot aún no terminó de conectar al gateway devuelve `{ "shared": [] }` (reintentar).

#### `GET /api/guilds/{guildId}/config` — snapshot completo de un servidor
```json
{
  "guildId": "1475204967567589440",
  "moderation": { "logChannelId": "…" },
  "welcome":    { "enabled": false, "channelId": "…", "message": "…" },
  "voice":      { "hubChannelId": "…", "tempChannelNameTemplate": "…" },
  "music":      { "volume": 25, "djRoleId": "…" },
  "ai":         { "chatEnabled": true, "mentionsEnabled": false, "spontaneousEnabled": false },
  "downloads":  { "enabled": true },
  "blockedChannels": ["…"],
  "counting":   { "enabled": true, "channelId": "…", "base": "Decimal|Binario|Octal|Hexadecimal",
                  "goal": 100, "extraChancesPerDay": 1,
                  "emojiCorrect": "✅", "emojiIncorrect": "❌", "emojiRecord": "🎉",
                  "loseMessage": "…",
                  "currentValue": 42, "currentRecord": 57 },
  "youTube":    { "channelId": "UC…", "channelName": "…", "notifyChannelId": "…",
                  "notifyRoleId": "…", "customMessage": "…" }
}
```
`counting` y `youTube` son `null` si esa feature nunca se configuró en el servidor. Los campos que valen `null` = sin configurar. `blockedChannels` son los canales en lockdown con `/bloquear` (solo lectura desde el panel; se gestionan con los comandos).

#### `POST /api/guilds/{guildId}/config` — editar ajustes generales
Body (todos opcionales, `null` = no tocar; `""` = quitar):
```json
{
  "modLogChannelId": "…", "welcomeChannelId": "…", "welcomeMessage": "…",
  "hubChannelId": "…", "tempChannelNameTemplate": "…",
  "volume": 25, "djRoleId": "…",
  "geminiChatEnabled": true, "geminiMentionsEnabled": false,
  "geminiSpontaneousEnabled": false, "downloadsEnabled": true
}
```
Respuesta `200`: el snapshot completo actualizado (igual que el GET).

#### `POST /api/guilds/{guildId}/config/counting` — editar juego de conteo
```json
{
  "channelId": "…", "base": "Hexadecimal", "goal": 100,
  "extraChancesPerDay": 2,
  "emojiCorrect": "✅", "emojiIncorrect": "❌", "emojiRecord": "🎉",
  "loseMessage": "…"
}
```
Respuesta `200`: snapshot completo. Nota: `currentValue`/`currentRecord` son de solo lectura (los gestiona el bot).

#### `POST /api/guilds/{guildId}/config/youtube` — crear/editar suscripción YouTube
```json
{
  "ytChannelId": "UC…", "ytChannelName": "…",
  "notifyChannelId": "…", "notifyRoleId": "…", "customMessage": "…"
}
```
Respuesta `200`: snapshot completo. Si el servidor no tenía suscripción, la crea (el bot hará backfill silencioso del feed).

#### `DELETE /api/guilds/{guildId}/config/youtube` — quitar suscripción YouTube
`204 No Content` si existía; `404` si no había.

### Notas para el frontend
- Tras guardar, la caché del bot (`GuildSettingsService`) se invalida sola: los cambios aplican de inmediato en Discord.
- El guardado de `volume` se acota a 0-100 y el de `extraChancesPerDay` a 0-10 en el backend.
- Los IDs de canal/rol deben ser de recursos del MISMO servidor indicado en la ruta (el backend no lo valida todavía; sí falla al usarlos si no existen).
- Falta por construir (pendiente): OAuth de Discord para saber QUIÉN administra qué servidor; hoy la única protección es la API key.

---

## 19. Registro de cambios — 2026-08-13 (lockdown de canales: /bloquear y /desbloquear)

### Solicitud
Comandos `/bloquear` (canal específico o el actual) y `/desbloquear` para impedir que nadie hable en un canal — estilo `/lock` de los bots comerciales (Carl-bot, Dyno).

### Cambios realizados
- **`Data/Entities/ChannelLock.cs` (nuevo):** guarda por canal bloqueado el overwrite ORIGINAL de @everyone (`AllowBits`/`DenyBits`, `HadOverwrite`) para restaurarlo exactamente al desbloquear. PK `ChannelId`, índice `GuildId`. Migración `20260813024549_AddChannelLocks` (creada con `dotnet-ef` — volvió a funcionar tras instalar el runtime ASP.NET).
- **`Services/ChannelLockService.cs` (nuevo):** `BloquearAsync` niega `SendMessages+AddReactions` en texto/news/foro y `UseVoice` en voz/escenario, preservando los permisos previos del overwrite. `DesbloquearAsync` restaura el overwrite original exacto, o si no había uno, quita solo los bits del bloqueo (y borra el overwrite si queda vacío). `ListarAsync` para el panel.
- **`Modules/LockModule.cs` (nuevo):** `/bloquear` y `/desbloquear` con `[SlashRequirePermissions(ManageChannels)]` + `[SlashRequireBotPermissions(ManageRoles)]`, con verificación ADICIONAL de permisos del usuario y del bot sobre el canal destino (los overrides de canal pueden quitarlos). Solo canales de texto/voz (el resto → error).
- **Panel web:** el snapshot (`GET /api/guilds/{id}/config`) ahora incluye `blockedChannels` (IDs de canales en lockdown, solo lectura) y `/config ver` muestra la lista en Discord.
- **messages.json:** nueva sección `Bloqueo` (7 claves).

### Notas técnicas
- El API real de DSharpPlus 5 para overwrites es `AddOverwriteAsync(DiscordRole, Permissions allow, Permissions deny, string reason)` y `DeleteOverwriteAsync(DiscordRole, string reason)` — NO existe overload con `DiscordOverwriteBuilder` en canales existentes.
- `ChannelType.Forum` no existe en DSharpPlus 5; el foro se llama `ChannelType.GuildForum`.
- Cambiar overwrites exige al BOT el permiso "Gestionar roles" (ManageRoles) en ese canal, no "Gestionar canales".

### Validación
- Compilación limpia y desplegado en Fly.io; migración `AddChannelLocks` aplicada automáticamente al arrancar (verificada en `/app/data/snowflake.db`).
- Bot conectado (gateway + Lavalink) y API respondiendo tras el deploy.
- Pendiente de probar en Discord: bloquear/desbloquear un canal real y comprobar que un usuario sin roles no puede hablar.

---

## 20. Registro de cambios — 2026-08-13 (i18n: bot trilingüe en/es/pt)

### Solicitud
Traducir todos los mensajes y comandos al inglés, crear opción de idioma por servidor (cambiable con `/lang` o desde el portal web), 3 idiomas (inglés, español, portugués), inglés por defecto, y que toda feature futura tenga sus 3 versiones.

### Cambios realizados
- **Idioma por servidor:** `GuildConfig.Language` (default "en") + migración `AddGuildLanguage`. `/lang` (ManageGuild) con choices English/Español/Português; muestra el actual sin argumento. El panel web: `language` en el snapshot y en el `GuildConfigPatch` (validado con `Languages.Normalizar`). `/config show` muestra el idioma.
- **Mensajes:** `messages.json` dividido en **3 archivos** (`messages.en.json` base, `messages.es.json`, `messages.pt.json`, 194 claves cada uno; copiados junto al ejecutable y con recarga en caliente). `MessagesService` reescrito: `Get(guildId, clave, ph)` (idioma del servidor desde la caché de `GuildSettingsService.Locale`, fallback a inglés), `Get(locale, ...)`, `En(...)` y `Locale(guildId)`.
- **Comandos slash en inglés** (nombres canónicos) + localizaciones es/pt vía `[NameLocalization(Localization.Spanish|Portuguese, "…")]` y `[DescriptionLocalization(...)]` en comandos, grupos Y opciones (DSharpPlus 5 admite target `Parameter`). Renombres: `/kick /ban /timeout /untimeout /warn /history /welcome /download /channel /colors /m queue|pause|resume|volume /talk /talk-clear /gemini-spontaneous /counting channel|chances|goal|icons|lose-message|stats /youtube follow|unfollow|show|role|channel|message /lock /unlock /config show|log-channel /lang` (los usuarios con cliente en español/portugués ven los nombres traducidos automáticamente).
- **Deshardcodeo de idioma en código:** `DurationParser.Format(ts, locale)` (unidades localizadas), `YouTubeNotifyService.ConstruirRelativo` ahora usa claves `YouTube:Hace*`, "🔴 LIVE" → `Musica:EnVivo`, nota de truncado → `Chat:Truncada`, errores de Gemini en inglés (ChatModule comprueba el nuevo mensaje "GEMINI_API_KEY environment variable is missing."), prompt espontáneo pide el idioma del servidor (`Languages.Nombre(locale)`), `SystemPrompt` por defecto en inglés (responde en el idioma del usuario).
- **Todos los call sites** de `_msg.Get`/`msg.Get` pasan ahora el guildId (o locale).
- **`AGENTS.md` nuevo** con la regla i18n permanente y convenciones del proyecto.

### Notas técnicas
- Los nombres de comando localizados los muestra Discord según el idioma del CLIENTE de cada usuario (no por servidor): es la forma correcta de "3 versiones del comando". El idioma de los MENSAJES sí es por servidor.
- `GuildSettingsService.Locale(guildId)` es síncrono (lee la caché precargada en `GuildDownloadCompleted`); si la caché está fría devuelve inglés (el default).
- La clave `DEEPSEEK_API_KEY` ausente se detecta comparando el mensaje exacto en inglés; si se cambia el texto hay que actualizar el chequeo en `ChatModule`.

### Validación
- Compilación limpia, migración `AddGuildLanguage` aplicada automáticamente en Fly.io, snapshot con `language`, ciclo de idioma probado en producción vía API (es → en).
- Desplegado en Fly.io con el bot conectado (gateway + Lavalink). Pendiente de probar en Discord: `/lang` y ver los nombres localizados en clientes con idioma español/portugués.

---

## 21. Registro de cambios — 2026-08-13 (IA: DeepSeek sustituye a Gemini)

### Solicitud
Cambiar por completo el módulo de IA para usar el modelo **DeepSeek v4 flash**, y dejar el system prompt **siempre en inglés** (nunca traducido) para evitar inconsistencias del modelo.

### Cambios realizados
- **`Services/DeepSeekService.cs` (nuevo, sustituye a GeminiService):** API de DeepSeek (`POST https://api.deepseek.com/chat/completions`, formato OpenAI-compatible: `Authorization: Bearer`, body `{model, messages, temperature, max_tokens, stream:false}`). Se conserva toda la lógica: conversación compartida por server, semáforo + límite de concurrencia por guild, historial recortado, modo espontáneo con umbral, `_mensajesGenerados`. Errores `DeepSeekException`/`DeepSeekBusyException`.
- **`Configuration/DeepSeekOptions.cs` (nuevo):** `Model` por defecto **`deepseek-v4-flash`** (sobrescribible con `DEEPSEEK_MODEL`); `SystemPrompt` SIEMPRE en inglés (comentado en código como excepción intencionada a la regla i18n). Se elimina la mención a Google Search (DeepSeek no tiene esa tool). Sección `DeepSeek` en appsettings.json.
- **Variables de entorno:** `DEEPSEEK_API_KEY` (obligatoria; se eliminó la dependencia de `GEMINI_API_KEY`) y `DEEPSEEK_MODEL` (opcional). `.env.example` actualizado. Secreto configurado en Fly.io.
- **Referencias actualizadas:** `DiscordBotService`, `ChatModule`, `Program.cs` (HttpClient "DeepSeek", singleton, options), `appsettings.json`.
- **Comandos renombrados:** `/gemini-menciones` → `/ai-mentions` (es: `/ia-menciones`, pt: `/ia-mencoes`) y `/gemini-espontaneo` → `/ai-spontaneous` (es/pt: `/ia-espontaneo`).
- **Mensajes (3 idiomas):** `Chat:SinApiKey/MencionesFaltaApiKey/EspontaneoFaltaApiKey` ahora citan `DEEPSEEK_API_KEY`; `Config:VerAi` → "IA (DeepSeek)"; `Chat:ErrorDebug` → "DeepSeek error".

### Regla nueva (documentada en AGENTS.md)
- El **system prompt** (DeepSeekOptions.SystemPrompt + appsettings.json) se mantiene SIEMPRE en inglés: no se localiza ni se traduce, para evitar incongruencias del modelo. La excepción a la regla i18n es deliberada.

### Validación
- Compilación limpia, desplegado en Fly.io con el secreto `DEEPSEEK_API_KEY` activo (estado "Deployed"), bot conectado (gateway + Lavalink), API online.
- Pendiente de probar en Discord: `/talk`, `/ai-mentions` y `/ai-spontaneous` con una clave de DeepSeek válida.

---

## 22. Registro de cambios — 2026-08-13 (búsqueda en internet para la IA)

### Solicitud
Integrar búsqueda web al modelo de forma automática pero no siempre (a criterio del modelo), con opción de desactivarla por servidor si los administradores lo quieren.

### Cambios realizados
- **Migración a la Responses API:** `DeepSeekService` ahora usa `POST https://api.deepseek.com/responses` (formato OpenAI). Request: `{model, instructions (system prompt), input, tools, tool_choice, temperature, max_output_tokens, stream}`.
- **Búsqueda web nativa (`web_search`):** cuando el servidor tiene `AiWebSearchEnabled`, se envía `tools: [{"type": "web_search"}]` con **`tool_choice: "auto"`** — el modelo decide cuándo buscar (la búsqueda la ejecuta el servidor de DeepSeek, como el antiguo googleSearch de Gemini). Desactivado → sin tools.
- **Historial compartido rediseñado:** de tuplas `(role, texto)` a items JSON de la Responses API (`message` + `web_search_call`); los items de salida se devuelven tal cual en el siguiente turno (la API restaura los resultados de la búsqueda automáticamente). Recorte del historial por número de mensajes de usuario.
- **Toggle por servidor:** `GuildConfig.AiWebSearchEnabled` (default true) + migración `AddGuildAiWebSearch`. Comando `/ai-search` (ManageGuild; es: `/ia-busqueda`, pt: `/ia-busca`), campo `ai.webSearchEnabled` en el snapshot y en el `GuildConfigPatch` del panel, y `/config show` lo muestra (línea "Búsqueda").
- **System prompt (inglés, sin traducir):** añadida la instrucción "usa la búsqueda web cuando la pregunta necesite información actual; si no, responde con tu conocimiento".
- **Modo espontáneo:** llamada puntual SIN búsqueda (un comentario casual no la necesita).
- **Mensajes nuevos (3 idiomas):** `Chat:BusquedaActivada/BusquedaDesactivada`; `Config:VerAiDetalle` actualizado con `{busqueda}`.

### Validación
- Compilación limpia, migración aplicada automáticamente en Fly.io, snapshot con `webSearchEnabled`, toggle probado vía API (off→on).
- Pendiente de probar en Discord: `/talk` con una pregunta de actualidad (debería disparar web_search solo cuando el modelo lo juzgue) y `/ai-search off`.

---

## 23. Registro de cambios — 2026-08-13 (IA: sin idioma forzado)

### Solicitud
Aclaración y ajuste: el usuario preguntó si los mensajes del chat IA se traducían automáticamente. No hay capa de traducción (el modelo genera directamente), pero había dos instrucciones que forzaban idioma; se pidió eliminarlas para que el modelo hable en el idioma que crea conveniente.

### Cambios realizados
- **Prompt espontáneo:** eliminada la instrucción `"Reply ... in {idioma del servidor}"` y el parámetro `locale` de `GenerarComentarioEspontaneoAsync` (y su call site en DiscordBotService). El modelo elige el idioma del comentario por sí mismo.
- **System prompt** (`DeepSeekOptions` + `appsettings.json`): eliminada la instrucción "Reply in the language the user writes in (default to English)"; ahora solo describe tono/estilo sin condicionar idioma.
- Actualizado `AGENTS.md`: el idioma de SALIDA de la IA no se fuerza de ninguna manera.

---

## 24. Registro de cambios — 2026-08-14 (la IA ejecuta comandos del bot + volumen matemático)

### Solicitud
1. Que la IA entienda instrucciones en el chat y las interprete como comandos del bot (todos), con permisos reales y output en embed.
2. Flujo conversacional: petición indirecta ("se escucha muy alto") → la IA pregunta y actúa al confirmar; petición directa ("bájale 10pts") → ejecuta de una; comandos destructivos → confirmación con botones [Aceptar]/[Rechazar], efímera (solo quien la pidió), rechazo automático a los 15 s.
3. `/m volume` con matemáticas simples (absoluto, relativo `-10`/`+5`, expresiones `30+20`, `100/2`).
4. Toggle por servidor para los comandos por IA (ON por defecto). UX: texto plano del modelo + embed solo con el output del comando.

### Cambios realizados
- **`Services/AiCommands/AiCommandExecutor.cs` (+ `.Otros.cs`) (nuevo):** catálogo declarativo de tools — moderación (`ban_user`, `kick_user`, `timeout_user`, `untimeout_user`, `warn_user`, `get_user_history`), canales (`lock_channel`, `unlock_channel`, `clear_messages`), música (`music_play/skip/pause/resume/stop/volume`), config (`welcome_*`, `counting_*`, `youtube_follow/unfollow`, `colors_install/uninstall`, `channel_create`, `logchannel_set`) y `get_server_state` (solo lectura). Cada tool replica las auditorías del slash command (permisos guild y de canal destino, jerarquía, mismo canal/DJ, no al bot/owner) y reutiliza las claves de mensajes (i18n automática). Destructivos: ban/kick/timeout/warn/clear.
- **`Services/AiCommandConfirmation.cs` (nuevo):** confirmación con botones (custom_id `snowflake_ai_confirm_{token}_ok|_no`); efímera vía followup en `/talk`, mensaje normal en menciones; solo el usuario solicitante puede pulsar; timeout 15 s con rechazo automático; al resolver, deshabilita botones y reanuda al modelo.
- **`DeepSeekService`:** bucle tool-call (máx. 5 iteraciones por turno, p. ej. `get_server_state` → `music_volume` → texto); `PreguntarAsync(AiCommandContext, …)` devuelve `AiChatOutcome { Texto, Comandos[], Pendiente? }`; `ReanudarToolAsync` para aceptar/rechazar; `DeepSeekConfirmationPendingException`; el historial guarda `function_call`/`function_call_output`; modo espontáneo sigue sin tools.
- **ChatModule/DiscordBotService:** publicación texto + embeds (`ChatModule.ConstruirEmbedComando`: título = comando, descripción = output, verde/rojo); pendientes: pre-texto + confirmación; registro del mensaje final como generado (respondible).
- **`/m volume` matemático:** opción pasa a string; `MusicService.TryParseVolumen` (relativo siempre si empieza por +/-; expresiones de un operador; sin eval) + `ObtenerVolumenActualAsync` (persistido ?? 100); error `Musica:VolumenInvalido`.
- **Toggle:** `GuildConfig.AiCommandsEnabled` (default true) + migración `AddGuildAiCommands` + `/ai-commands` (es/pt `ia-comandos`) + `ai.commandsEnabled` en snapshot/patch del panel + línea en `/config show`.
- **System prompt (inglés):** guía de cuándo ejecutar directo, cuándo preguntar y cuándo llamar a tools destructivas (la autorización la gestiona el bot).
- **Refactor compartido:** `ModerationLogService.AvisarPrivadoAsync` y `RegistrarAsync(con id+tag)`; `MusicService.ValidarControlAsync` (DJ/mismo canal) usado también por `MusicModule`.
- **Mensajes nuevos (3 idiomas):** `Chat:ComandoCancelado/ComandoExpirado/Confirmacion* (6)/ComandosActivados/Desactivados/ErrorEjecucion`, `Musica:VolumenInvalido`, `Config:VerAiDetalle` con `{comandos}`.

### Notas
- Descarga (`/download`) queda FUERA del catálogo de IA (produce archivos y es pesada); se puede añadir luego. Los comandos meta (`/lang`, `/ai-*`, `/config`) tampoco.
- Los IDs que el modelo pasa se sanitizan: usuarios por mención/ID/nombre, canales por mención/ID/"current"/nombre.

### Validación
- Compilación limpia; migración aplicada automáticamente en Fly.io; snapshot con `commandsEnabled: true`; bot online sin errores en el log.
- Pendiente de probar en Discord: petición directa ("bájale 10pts a la música" → ejecuta), indirecta (pregunta y confirma), destructiva (botones + timeout) y `/m volume nivel:-10`.
