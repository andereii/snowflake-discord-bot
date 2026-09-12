# ❄️ Snowflake

Bot de Discord multifunción escrito en **C# / .NET 10**. Incluye moderación, música, utilidades, juegos, chatbot con IA, notificaciones de YouTube y un panel web de configuración en el mismo proceso.

## ✨ Características

### Moderación y servidor
- 🛡️ Moderación documentada: advertencias, expulsión, veto, aislamiento, softban y hardmute, con historial y canal de logs.
- 🔒 Bloqueo/desbloqueo de canales y limpieza de mensajes.
- 👋 Mensajes de bienvenida configurables.
- 🎨 Roles de colores autoasignables.
- 🎧 Canales de voz temporales (*join-to-create*).
- ⚙️ Ajustes por servidor (`/config`, `/lang`) — trilingüe **en / es / pt**.

### Música y media
- 🎵 Música con Lavalink (YouTube y canciones de Spotify; playlists/álbumes con credenciales de Spotify).
- 🔊 Volumen persistente por servidor (absoluto, relativo o expresiones simples).
- 🔀 Control de cola: play, skip, pause, resume, stop, shuffle, seek y rol DJ.
- 📥 Descargas de vídeo/audio con `yt-dlp` (cookies opcionales).
- 🖼️ Búsqueda de imágenes (`/image`).

### IA
- 💬 Chatbot con **DeepSeek** (prioridad) o **Gemini** si hay clave: `/talk`, respuestas a menciones `@` y modo espontáneo.
- 🌐 Búsqueda web opcional a criterio del modelo (toggle por servidor).
- 🤖 La IA puede ejecutar comandos del bot (música, moderación, config, etc.) con confirmación en acciones destructivas.

### Juegos y utilidades
- 🔢 Juego de conteo (bases decimal/binario/octal/hex, récords, oportunidades extra, leaderboard).
- 📊 Encuestas nativas (`/encuesta`) con gráfica circular generada en Dlang.
- 📺 Notificaciones de canales de YouTube (feed RSS).
- 🎲 Dados, calculadora, trivia, AFK, ping y más utilidades ligeras.

### Panel web y despliegue
- 🖥️ API REST de configuración alojada en el mismo proceso (`Sdk.Web`); frontend en `web/`.
- ☁️ Listo para **Fly.io** (`Dockerfile` + `fly.toml`; Lavalink como servicio auxiliar).

## 🧰 Stack

- .NET 10 · DSharpPlus 5 · ASP.NET (bot + API)
- Lavalink 4 + Lavalink4NET · LavaSrc / youtube-source
- SQLite + Entity Framework Core
- DeepSeek / Gemini · `yt-dlp` · `ffmpeg`
- Pie charts nativos en Dlang (`src/Dlang`)

## 🚀 Puesta en marcha

Requisitos: .NET 10, Java 17+, Lavalink, `ffmpeg` y `yt-dlp`.

1. Copia `.env.example` a `.env` y añade `DISCORD_TOKEN`.
2. Para la IA, añade al menos una clave: `DEEPSEEK_API_KEY` (prioridad) y/o `GEMINI_API_KEY`. Opcional: `AI_PROVIDER=deepseek|gemini`, `DEEPSEEK_MODEL`, `GEMINI_MODEL`.
3. En Discord Developer Portal → Bot, activa **Message Content Intent** (conteo, menciones y chat IA).
4. Opcional: `SPOTIFY_CLIENT_ID` / `SPOTIFY_CLIENT_SECRET` para playlists y álbumes de Spotify; `YT_COOKIES_FILE` para descargas con sesión; `WEB_PANEL_API_KEY` para proteger mutaciones del panel (`X-Api-Key`).
5. Arranca Lavalink y el bot:

```bash
./deploy/lavalink/run.sh
dotnet run --project src/Snowflake.Bot
```

Los comandos slash se registran en el servidor de pruebas de `appsettings.json`. Los textos editables están en `messages.en.json`, `messages.es.json` y `messages.pt.json` (recarga en caliente).

> 🔐 No subas `.env`, tokens, bases SQLite ni archivos generados. El `.gitignore` del proyecto ya los excluye.

Consulta [`CONTEXTO.md`](CONTEXTO.md) para arquitectura, comandos y el registro detallado del desarrollo.
