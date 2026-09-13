# Snowflake

Bot de Discord multifunción en **Node.js**. Incluye moderación, bienvenidas, música, IA, conteo, YouTube y un panel web.

## Stack

- Bot: `discord.js` 14 (`bot/`)
- Panel: Express + Passport Discord (`web-backend/`) y React + Vite (`web-frontend/`)
- SQLite (`data/snowflake.db`)
- IA: DeepSeek V4.1 Flash (`deepseek-flash`) con fallback a Gemini
- Descargas: `yt-dlp` + ffmpeg

## Puesta en marcha

Requisitos: Node.js 20+, `ffmpeg` y `yt-dlp`.

1. Copia `.env.example` a `.env` y añade `DISCORD_TOKEN`.
2. Añade `DEEPSEEK_API_KEY` (o `GEMINI_API_KEY`) para el chatbot.
3. En Discord Developer Portal → Bot, activa **Message Content Intent**.
4. En `web-backend/.env` configura `DISCORD_CLIENT_SECRET` para el login del panel.
5. Instala dependencias e inicia todo:

```bash
./install-deps.sh
./start.sh
```

Los textos del bot están en `bot/src/locales/messages.{en,es,pt}.json`. El idioma por servidor se cambia con `/lang` o desde el panel.

Consulta [`CONTEXTO.md`](CONTEXTO.md) para arquitectura, comandos y el registro detallado del desarrollo.
