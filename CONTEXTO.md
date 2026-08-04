# 📋 Dossier completo: Snowflake — Bot de Discord

> Documento de contexto para compartir el estado completo del proyecto con otra IA o desarrollador.
> Fecha: agosto 2026.

## 1. Resumen
Bot de Discord en **C# / .NET 10** con **DSharpPlus 5.0.0** que ofrece: moderación documentada, bienvenidas, descarga de vídeos/audio, paletas de colores autoasignables, creación de canales, música vía Lavalink y sistema join-to-create de canales de voz. El objetivo final es desplegarlo en un VPS propio. Todo el código está en español.

- **Nombre:** Snowflake (`Snowflake#3104`)
- **Client ID:** `1052318909035970641`
- **Guild de pruebas:** `1475204967567589440` (los comandos se registran SOLO aquí, para que aparezcan al instante)
- **Owner ID:** `553023040489914369`
- **Intents:** Guilds, GuildMembers, GuildBans, GuildVoiceStates, GuildMessages
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
├── .env / .env.example      # DISCORD_TOKEN (obligatorio), YT_COOKIES_FILE (opcional)
├── deploy/lavalink/
│   ├── Lavalink.jar         # servidor 4.2.2
│   ├── application.yml      # config + plugins (youtube-source + lavasrc)
│   └── run.sh               # java -jar Lavalink.jar
└── src/Snowflake.Bot/
    ├── Snowflake.Bot.csproj
    ├── Program.cs           # host, DI, arranque
    ├── appsettings.json     # Bot:{TestGuildId,OwnerId,Debug}, Lavalink:{Host,Port,Password}
    ├── messages.json        # TODOS los textos del bot (recarga en caliente)
    ├── Configuration/       # BotConfiguration, LavalinkOptions
    ├── Data/
    │   ├── BotDbContext.cs
    │   ├── BotDbContextFactory.cs  # IDesignTimeDbContextFactory para EF Migrations
    │   ├── Entities/        # Incident, GuildConfig, ColorRole, TempChannel
    │   └── Migrations/      # 20260804062107_InitialCreate (+ snapshot)
    ├── Modules/             # comandos slash
    ├── Services/            # lógica de negocio
    └── Utilities/           # DurationParser
```

**Ojo:** el repositorio git real es `/home/alex` (todo el home está sin commitear, rama `master` sin commits). El proyecto NO tiene git propio.

## 4. Arquitectura de arranque (`Program.cs`)
1. `Env.TraversePath().Load()` — carga `.env` (no sobreescribe vars del sistema).
2. Host genérico con `ContentRootPath = AppContext.BaseDirectory` (la config se copia al ejecutable, funciona desde cualquier cwd).
3. `Bot` → `IOptionsMonitor<BotConfiguration>` (permite hot-reload de `Debug`).
4. `messages.json` → `IConfiguration` con `reloadOnChange: true` + `MessagesService`.
5. `IDbContextFactory<BotDbContext>` → SQLite `snowflake.db` junto al ejecutable.
6. `AddLavalink()` + `ConfigureLavalink` (`BaseAddress = http://host:port`, `Passphrase`, `Label = "snowflake"`).
7. Servicios singleton: ModerationLog, Download, Litterbox, Color, VoiceHub, Music, MusicWidget.
8. `DiscordClient` con los intents (token de `DISCORD_TOKEN`, lanza excepción clara si falta).
9. `DiscordBotService` (BackgroundService) registra los módulos slash con `UseSlashCommands` y `RegisterCommands(assembly, TestGuildId)`.
10. Al arrancar: `db.Database.MigrateAsync()` (EF Migrations; se migró desde `EnsureCreatedAsync`).

## 5. Base de datos SQLite (EF Core)
Tablas (todas con migración InitialCreate aplicada):

**`Incidents`** — historial de moderación (número de caso autoincremental):
`Id`, `GuildId`, `TargetUserId`, `TargetTag`, `ModeratorId`, `ModeratorTag`, `Type` (enum guardado como string: Advertencia/Expulsion/Veto/Aislamiento/FinAislamiento), `Reason`, `Duration TimeSpan?` (solo aislamientos), `CreatedAt`. Índice `(GuildId, TargetUserId)`.

**`GuildConfigs`** — config por servidor (PK `GuildId`):
`ModLogChannelId ulong?`, `WelcomeChannelId ulong?`, `WelcomeMessage string?`, `HubChannelId ulong?`, `Volume int?` (0-100, persistente, null = 100 por defecto).

**`ColorRoles`** — roles de color instalados:
`Id`, `GuildId`, `RoleId`, `Name`, `ColorHex`. Único `(GuildId, RoleId)`.

**`TempChannels`** — canales temporales join-to-create:
`ChannelId` (PK), `GuildId`, `OwnerUserId`, `CreatedAt`.

**Gotcha conocido:** con `EnsureCreated` la BD no se actualiza al añadir columnas (hubo que borrarla a mano dos veces); por eso se pasó a EF Migrations.

## 6. Sistema de textos (`messages.json` + `MessagesService`)
- Todos los mensajes viven en `messages.json` con estructura de secciones: `Ping`, `Errores`, `Presentacion`, `Moderacion`, `Descargas`, `Bienvenida`, `Musica`, `Colores`, `Voces`, `Config`.
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

**Reglas de moderación (importantes):** no auto-acciones, no al bot, no al owner, jerarquía de roles (`miembro.Hierarchy >= bot.Hierarchy` → error), DM de aviso best-effort (si tiene MD cerrados sigue), cada acción crea un `Incident` y se anuncia en el canal de logs con embed de color por tipo + número de caso + timestamp.

## 8. Servicios y eventos

**`DiscordBotService`** (BackgroundService, el corazón):
- `Ready`, `GuildDownloadCompleted` (logs), `GuildCreated` → presenta al bot en el system channel o primer canal de texto donde pueda escribir.
- `GuildMemberAdded` → bienvenida (usa config de BD o mensaje por defecto; ignoran bots).
- `VoiceStateUpdated` → delega en `VoiceHubService`.
- `ComponentInteractionCreated` → **router de componentes**: `snowflake_colores` → ColorService, `snowflake_music_*` → MusicWidgetService.
- `SlashCommandErrored` → si es `SlashExecutionChecksFailedException` → "SinPermisos"; si no, mensaje de error con detalles solo en modo `Debug` (fallback a follow-up si ya había respuesta).

**`VoiceHubService`** (join-to-create): al entrar al canal hub → crea canal `🎧 {usuario}` en la misma categoría, con overwrites al dueño (ManageChannels, MoveMembers, MuteMembers, DeafenMembers, AccessChannels, UseVoice), lo mueve dentro y lo registra en `TempChannels`. Al salir de un canal temporal → si queda vacío lo borra y elimina el registro. Ignora cambios de mute/deafen.

**`ColorService`**: dos paletas de 17 colores hardcodeadas (Normal: Rojo→Negro; Pastel: Rosa pastel→Perla). `InstalarAsync` reemplaza paletas cruzadas (borra roles de la otra paleta y crea los que falten). Roles con prefijo `• `. `HandleSelectAsync` gestiona el menú (quita el color anterior, asigna el nuevo).

**`MusicService`**: lógica Lavalink4NET (ver sección 9).

**`MusicWidgetService`**: widget "reproduciendo ahora" (embed + 4 botones: ⏯️ pausa, ⏭️ skip, 📋 cola, ⏹️ stop; custom_ids `snowflake_music_pause/skip/cola/stop`). Un widget por guild, guardado en `ConcurrentDictionary<guildId, (messageId, channelId)>`. **No se autoactualiza** — solo se refresca ante acciones del usuario (decisión de diseño deliberada, sin barra de progreso viva). Stop deja el widget estático con botones deshabilitados. El botón cola responde efímero.

**`DownloadService`**: lanza `yt-dlp` como proceso externo con args `--no-playlist --no-progress --no-warnings --no-part --restrict-filenames --print after_move:filepath -o {plantilla}`, cookies opcionales de `YT_COOKIES_FILE`, modo audio = `-x --audio-format mp3 --audio-quality 0`. Timeout duro 5 min, plantilla `%(title).80B [%(id)s].%(ext)s` en `/tmp/snowflake/{guid}/`. Errores → `YtDlpException` con las últimas 3 líneas de stderr saneadas (máx 800 chars). El llamador limpia el temp dir en `finally`.

**`LitterboxService`**: multipart POST a `litterbox.catbox.moe` con `reqtype=fileupload`, `time=72h`. Devuelve la URL (validada).

**`ModerationLogService`**: `RegistrarAsync` guarda el Incident (devuelve con Id), `AnunciarAsync` lo publica si hay canal de logs, `CrearEmbedIncidente` (colores por tipo: amarillo/naranja/rojo/morado/verde).

**`DurationParser`**: regex `^(\d+)\s*([smhd])$`, formato legible "3 día(s) / 2 hora(s) / 15 minuto(s) / 30 segundo(s)".

**`MessagesService`**: ver sección 6.

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
**Funcionando:** todo lo de las secciones 7-8, Lavalink con youtube-source + LavaSrc, reproducción normal de YouTube, volumen persistente por servidor y enlaces de canciones individuales de Spotify mediante el fallback oEmbed → búsqueda YouTube del bot. Las playlists/álbumes de Spotify dependen de credenciales de Spotify porque el token anónimo está fallando actualmente.

**Sesión actual — hecho:**
1. Añadido `Volume int?` a `GuildConfig`.
2. Creado `BotDbContextFactory` (IDesignTimeDbContextFactory).
3. Migrado `EnsureCreatedAsync()` → `MigrateAsync()`.
4. Migración `InitialCreate` generada (20260804062107) con la BD dev borrada.
5. `MusicService` ahora inyecta `IDbContextFactory<BotDbContext>`: aplica el volumen guardado al unirse al canal y `/m volumen` lo persiste en BD.
6. LavaSrc 4.8.3 añadido a `application.yml` y Lavalink reiniciado con él (verificado en log: "Loaded 'lavasrc-plugin-4.8.3.jar'", "Registering Spotify audio source manager...", "Lavalink is ready to accept connections").
7. Añadido el fallback de canciones individuales de Spotify en `MusicService`: oEmbed público → búsqueda YouTube.
8. `SPOTIFY_CLIENT_ID`/`SPOTIFY_CLIENT_SECRET` quedaron como variables opcionales en `application.yml`; `run.sh` carga automáticamente el `.env` del proyecto antes de arrancar Lavalink.
9. El bot fue relanzado y verificado: migración aplicada, base SQLite creada, conexión a Discord y nodo Lavalink establecidos. La compilación queda limpia.

**Validado:** el usuario confirmó que el volumen persistente y los enlaces de canciones de Spotify funcionan correctamente después de los cambios.

## 12. Pendientes / roadmap
- [x] Probar en Discord el volumen persistente y la reproducción de enlaces de canciones de Spotify.
- [ ] Añadir `SPOTIFY_CLIENT_ID` y `SPOTIFY_CLIENT_SECRET` al `.env` desde una aplicación de Spotify Developer si se quieren usar playlists/álbumes de Spotify sin depender del token anónimo.
- [ ] Fase 7: empaquetado VPS (systemd para el bot; Lavalink ya tiene run.sh, sin Docker).
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
