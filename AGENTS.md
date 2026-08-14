# AGENTS.md — convenciones del proyecto Snowflake (bot de Discord)

## REGLA OBLIGATORIA: i18n (en/es/pt)
El bot tiene **3 idiomas** (inglés por defecto): `en`, `es`, `pt`.

**Cada mensaje o comando nuevo DEBE tener las 3 versiones:**

1. **Mensajes:** en `src/Snowflake.Bot/messages.en.json`, `messages.es.json` y `messages.pt.json`, SIEMPRE con la MISMA clave. Se acceden con `MessagesService.Get(guildId, "Seccion:Clave", placeholders)` (idioma del servidor, fallback a inglés) — nunca con textos hardcodeados.
2. **Comandos slash:** nombre y descripción canónicos en INGLÉS + `[NameLocalization(Localization.Spanish, "…")]` / `[NameLocalization(Localization.Portuguese, "…")]` y `[DescriptionLocalization(...)]` en los tres idiomas. Las opciones (`[Option]`) también se localizan (los atributos admiten `Parameter`).
3. **Textos en código** (formateadores, prompts): usar las claves de messages o `Languages.*`; nunca cadenas en un solo idioma. `DurationParser.Format(ts, locale)` y `Languages.Nombre(locale)` ya existen.

El idioma por servidor está en `GuildConfig.Language` ("en"/"es"/"pt"), se cambia con `/lang` o por la API (`POST /api/guilds/{id}/config` con `{"language": "es"}`).

**EXCEPCIÓN deliberada:** el **system prompt de la IA** (`DeepSeekOptions.SystemPrompt` / sección "DeepSeek" de appsettings.json) va SIEMPRE en inglés y nunca se localiza, para evitar incongruencias del modelo. **No se fuerza ningún idioma de salida:** el modelo elige libremente en qué idioma responde (nada de instrucciones de idioma en prompts).

**IA:** el chatbot usa la Responses API de DeepSeek (`https://api.deepseek.com/responses`) con la tool nativa `web_search` y `tool_choice: "auto"` (el modelo decide cuándo buscar). Toggle por servidor: `GuildConfig.AiWebSearchEnabled` (`/ai-search` o panel web). El historial compartido guarda los items de la API tal cual (`message` + `web_search_call` + `function_call`/`function_call_output`) para el contexto multi-turno.

**Comandos por IA:** el modelo puede ejecutar comandos del bot vía function calling (`Services/AiCommands/AiCommandExecutor` — catálogo declarativo; las tools NUNCA ejecutan sin validar permisos reales: mismas reglas que los slash commands). Destructivos (ban/kick/timeout/warn/clear): confirmación con botones + timeout 15 s (`AiCommandConfirmation`). Toggle por servidor: `GuildConfig.AiCommandsEnabled` (`/ai-commands` o panel web). Todo tool nuevo debe: definirse en el catálogo, validar permisos como su slash command, y reusar claves de mensajes existentes (i18n automática).

## Build, migraciones y despliegue
- `dotnet build src/Snowflake.Bot` (Sdk.Web; para EJECUTAR en local hace falta el runtime ASP.NET).
- Migraciones: `export PATH="$PATH:$HOME/.dotnet/tools" && dotnet ef migrations add Nombre --project src/Snowflake.Bot --output-dir Migrations` y **rebuild** después (EF Core 10 lanza `PendingModelChangesWarning` si no).
- Despliegue: `~/.fly/bin/fly deploy --ha=false`. **Producción = Fly.io** (`snowflake-discord-bot-floral-river-8992`); NO ejecutar otra instancia local con el mismo token (gateway conflictivo). BD de producción: volumen `/app/data/snowflake.db`; las migraciones se aplican solas al arrancar.
- El bot registra los slash commands SOLO en `Bot:TestGuildId` (guild de pruebas) — los comandos aparecen al instante ahí.

## Arquitectura (resumen)
- Ajustes por servidor: **solo** vía `Services/Settings/GuildSettingsService` (caché TTL; punto único para bot + panel web). Nada de escribir `GuildConfigs` directamente desde módulos.
- Módulos heredan de `Modules/SnowflakeModuleBase` (helpers Responder/SafeEdit/ResponderError).
- API REST del panel: `Endpoints/` (`ConfigEndpoints`, `BotInfoEndpoints`, guarda `ApiKeyGuard`). IDs de Discord como string en JSON.
- Auditoría de comandos peligrosos: verificar permisos **en el canal destino** (overrides), no solo con `[SlashRequirePermissions]` (que también valida al bot). Control de música: mismo canal de voz / rol DJ / ManageGuild.
- Documentación completa del proyecto: `CONTEXTO.md` (dossier, secciones por feature).
