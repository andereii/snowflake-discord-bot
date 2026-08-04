# ❄️ Snowflake

Bot de Discord multifunción escrito en **C#/.NET 10**. Incluye moderación, bienvenidas, utilidades, canales temporales y música con Lavalink.

## ✨ Funciones

- 🎵 Música desde YouTube y enlaces de canciones de Spotify.
- 🔊 Volumen persistente por servidor mediante SQLite.
- 🛡️ Moderación documentada con historial y canal de logs.
- 👋 Mensajes de bienvenida configurables.
- 🎨 Roles de colores.
- 🎧 Canales de voz temporales (*join-to-create*).
- 📥 Descargas de vídeo/audio con `yt-dlp`.

## 🧰 Stack

- .NET 10 · DSharpPlus 5
- Lavalink 4 + Lavalink4NET
- SQLite + Entity Framework Core
- `youtube-source`, LavaSrc y `yt-dlp`

## 🚀 Puesta en marcha

Requisitos: .NET 10, Java 17+, Lavalink, `ffmpeg` y `yt-dlp`.

1. Copia `.env.example` a `.env` y añade `DISCORD_TOKEN`.
2. Si quieres playlists/álbumes de Spotify, añade también `SPOTIFY_CLIENT_ID` y `SPOTIFY_CLIENT_SECRET`.
3. Arranca Lavalink y el bot:

```bash
./deploy/lavalink/run.sh
dotnet run --project src/Snowflake.Bot
```

Los comandos slash se registran en el servidor de pruebas configurado en `appsettings.json`. Los textos editables están en `src/Snowflake.Bot/messages.json`.

> 🔐 No subas `.env`, tokens, bases SQLite ni archivos generados. El `.gitignore` del proyecto ya los excluye.

Consulta [`CONTEXTO.md`](CONTEXTO.md) para conocer la arquitectura, los comandos y el registro detallado del desarrollo.
