import 'dotenv/config';
import { REST, Routes, Client, GatewayIntentBits } from 'discord.js';
import fs from 'fs';
import path from 'path';

const commandsPath = path.join(process.cwd(), 'src', 'commands');
const commandFiles = fs.readdirSync(commandsPath).filter(file => file.endsWith('.js') && file !== 'index.js');

const nameLocalizations = {
    'talk': { 'es-ES': 'charlar', 'es-419': 'charlar', 'pt-BR': 'conversar' },
    'talk-clear': { 'es-ES': 'charlar-limpiar', 'es-419': 'charlar-limpiar', 'pt-BR': 'conversar-limpar' },
    'ai-mentions': { 'es-ES': 'ia-menciones', 'es-419': 'ia-menciones', 'pt-BR': 'ia-mencoes' },
    'ai-search': { 'es-ES': 'ia-busqueda', 'es-419': 'ia-busqueda', 'pt-BR': 'ia-busca' },
    'ai-commands': { 'es-ES': 'ia-comandos', 'es-419': 'ia-comandos', 'pt-BR': 'ia-comandos' },
    'play': { 'es-ES': 'reproducir', 'es-419': 'reproducir', 'pt-BR': 'tocar' },
    'skip': { 'es-ES': 'saltar', 'es-419': 'saltar', 'pt-BR': 'pular' },
    'stop': { 'es-ES': 'detener', 'es-419': 'detener', 'pt-BR': 'parar' },
    'pause': { 'es-ES': 'pausar', 'es-419': 'pausar', 'pt-BR': 'pausar' },
    'resume': { 'es-ES': 'reanudar', 'es-419': 'reanudar', 'pt-BR': 'retomar' },
    'volume': { 'es-ES': 'volumen', 'es-419': 'volumen', 'pt-BR': 'volume' },
    'ban': { 'es-ES': 'vetar', 'es-419': 'vetar', 'pt-BR': 'banir' },
    'kick': { 'es-ES': 'expulsar', 'es-419': 'expulsar', 'pt-BR': 'expulsar' },
    'timeout': { 'es-ES': 'aislar', 'es-419': 'aislar', 'pt-BR': 'isolar' },
    'warn': { 'es-ES': 'advertir', 'es-419': 'advertir', 'pt-BR': 'avisar' },
    'roll': { 'es-ES': 'dado', 'es-419': 'dado', 'pt-BR': 'dado' },
    'clear': { 'es-ES': 'limpiar', 'es-419': 'limpiar', 'pt-BR': 'limpar' },
    'afk': { 'es-ES': 'afk', 'es-419': 'afk', 'pt-BR': 'afk' },
    'poll': { 'es-ES': 'encuesta', 'es-419': 'encuesta', 'pt-BR': 'enquete' },
    'cat': { 'es-ES': 'gato', 'es-419': 'gato', 'pt-BR': 'gato' },
    'birthday': { 'es-ES': 'cumpleaños', 'es-419': 'cumpleaños', 'pt-BR': 'aniversario' },
    'birthday-remove': { 'es-ES': 'cumpleaños-quitar', 'es-419': 'cumpleaños-quitar', 'pt-BR': 'aniversario-remover' },
    'welcome': { 'es-ES': 'bienvenida', 'es-419': 'bienvenida', 'pt-BR': 'boas-vindas' },
    'lang': { 'es-ES': 'idioma', 'es-419': 'idioma', 'pt-BR': 'idioma' },
    'show': { 'es-ES': 'ver', 'es-419': 'ver', 'pt-BR': 'ver' },
    'download': { 'es-ES': 'descargar', 'es-419': 'descargar', 'pt-BR': 'baixar' },
    'image': { 'es-ES': 'imagen', 'es-419': 'imagen', 'pt-BR': 'imagem' },
    'untimeout': { 'es-ES': 'desaislar', 'es-419': 'desaislar', 'pt-BR': 'dessilenciar' },
    'history': { 'es-ES': 'historial', 'es-419': 'historial', 'pt-BR': 'historico' },
    'softban': { 'es-ES': 'softban', 'es-419': 'softban', 'pt-BR': 'softban' },
    'mute': { 'es-ES': 'mute', 'es-419': 'mute', 'pt-BR': 'mute' },
    'hardmute': { 'es-ES': 'hardmute', 'es-419': 'hardmute', 'pt-BR': 'hardmute' },
    'unhardmute': { 'es-ES': 'unhardmute', 'es-419': 'unhardmute', 'pt-BR': 'unhardmute' },
    'modlog': { 'es-ES': 'canal-logs', 'es-419': 'canal-logs', 'pt-BR': 'canal-de-logs' },
    'lock': { 'es-ES': 'bloquear', 'es-419': 'bloquear', 'pt-BR': 'bloquear' },
    'unlock': { 'es-ES': 'desbloquear', 'es-419': 'desbloquear', 'pt-BR': 'desbloquear' },
    'channel': { 'es-ES': 'canal', 'es-419': 'canal', 'pt-BR': 'canal' },
    'counting': { 'es-ES': 'conteo', 'es-419': 'conteo', 'pt-BR': 'contagem' },
    'trivia': { 'es-ES': 'trivia', 'es-419': 'trivia', 'pt-BR': 'trivia' },
    'youtube': { 'es-ES': 'youtube', 'es-419': 'youtube', 'pt-BR': 'youtube' },
};

const descriptionLocalizations = {
    'youtube': { 'es-ES': 'Suscripciones de notificaciones de YouTube', 'es-419': 'Suscripciones de notificaciones de YouTube', 'pt-BR': 'Inscrições de notificações do YouTube' },
    'counting': { 'es-ES': 'Configura y juega al conteo en el servidor', 'es-419': 'Configura y juega al conteo en el servidor', 'pt-BR': 'Configura e joga o jogo de contagem no servidor' },
    'trivia': { 'es-ES': 'Juega a la trivia cultural y consulta las clasificaciones', 'es-419': 'Juega a la trivia cultural y consulta las clasificaciones', 'pt-BR': 'Jogue jogos de curiosidades e veja o ranking' },
    'lock': { 'es-ES': 'Bloquea un canal: nadie podrá hablar en él (lockdown)', 'es-419': 'Bloquea un canal: nadie podrá hablar en él (lockdown)', 'pt-BR': 'Bloqueia um canal: ninguém poderá falar nele (lockdown)' },
    'unlock': { 'es-ES': 'Desbloquea un canal: restaura los permisos anteriores', 'es-419': 'Desbloquea un canal: restaura los permisos anteriores', 'pt-BR': 'Desbloqueia um canal: restaura as permissões anteriores' },
    'channel': { 'es-ES': 'Crea canales y configura el join-to-create', 'es-419': 'Crea canales y configura el join-to-create', 'pt-BR': 'Cria canais e configura o join-to-create' },
    'untimeout': { 'es-ES': 'Quita el aislamiento (timeout) a un usuario', 'es-419': 'Quita el aislamiento (timeout) a un usuario', 'pt-BR': 'Remove o silêncio de um usuário' },
    'history': { 'es-ES': 'Muestra los incidentes de un usuario (o los últimos del servidor)', 'es-419': 'Muestra los incidentes de un usuario (o los últimos del servidor)', 'pt-BR': 'Mostra os incidentes de um usuário (ou os últimos do servidor)' },
    'softban': { 'es-ES': 'Banea y desbanea al instante para borrar mensajes', 'es-419': 'Banea y desbanea al instante para borrar mensajes', 'pt-BR': 'Bane e desbane instantaneamente para apagar mensagens' },
    'mute': { 'es-ES': 'Silencia a un usuario (timeout)', 'es-419': 'Silencia a un usuario (timeout)', 'pt-BR': 'Silencia um usuário (timeout)' },
    'hardmute': { 'es-ES': 'Quita roles y revoca permisos de enviar/hablar en todos los canales', 'es-419': 'Quita roles y revoca permisos de enviar/hablar en todos los canales', 'pt-BR': 'Remove cargos e revoga permissões de enviar/falar em todos os canais' },
    'unhardmute': { 'es-ES': 'Restaura roles y permisos tras un hardmute', 'es-419': 'Restaura roles y permisos tras un hardmute', 'pt-BR': 'Restaura cargos e permissões após um hardmute' },
    'modlog': { 'es-ES': 'Establece el canal donde se anuncian los incidentes de moderación', 'es-419': 'Establece el canal donde se anuncian los incidentes de moderación', 'pt-BR': 'Define o canal onde os incidentes de moderação são anunciados' },
    'image': { 'es-ES': 'Busca una imagen en la web', 'es-419': 'Busca una imagen en la web', 'pt-BR': 'Busca uma imagem na web' },
    'download': { 'es-ES': 'Descarga un vídeo (o solo audio) de Internet con yt-dlp', 'es-419': 'Descarga un vídeo (o solo audio) de Internet con yt-dlp', 'pt-BR': 'Baixa um vídeo (ou só o áudio) da internet com yt-dlp' },
    'talk': { 'es-ES': 'Habla con la IA en la conversación compartida del servidor', 'es-419': 'Habla con la IA en la conversación compartida del servidor', 'pt-BR': 'Converse com a IA na conversa compartilhada do servidor' },
    'talk-clear': { 'es-ES': 'Reinicia la conversación compartida de la IA', 'es-419': 'Reinicia la conversación compartida de la IA', 'pt-BR': 'Reinicia a conversa compartilhada da IA' },
    'ai-mentions': { 'es-ES': 'Activa o desactiva las respuestas por mención (@)', 'es-419': 'Activa o desactiva las respuestas por mención (@)', 'pt-BR': 'Ativa ou desativa respostas por menção (@)' },
    'ai-search': { 'es-ES': 'Activa o desactiva la búsqueda web de la IA', 'es-419': 'Activa o desactiva la búsqueda web de la IA', 'pt-BR': 'Ativa ou desativa a busca web da IA' },
    'ai-commands': { 'es-ES': 'Activa o desactiva la ejecución de comandos por IA', 'es-419': 'Activa o desactiva la ejecución de comandos por IA', 'pt-BR': 'Ativa ou desativa a execução de comandos por IA' },
    'play': { 'es-ES': 'Reproduce una canción, playlist o archivo subido', 'es-419': 'Reproduce una canción, playlist o archivo subido', 'pt-BR': 'Toca uma música, playlist ou arquivo enviado' },
    'skip': { 'es-ES': 'Salta la canción actual', 'es-419': 'Salta la canción actual', 'pt-BR': 'Pula a música atual' },
    'stop': { 'es-ES': 'Detiene la música y vacía la cola', 'es-419': 'Detiene la música y vacía la cola', 'pt-BR': 'Para a música e limpa a fila' },
    'pause': { 'es-ES': 'Pausa la reproducción actual', 'es-419': 'Pausa la reproducción actual', 'pt-BR': 'Pausa a reprodução atual' },
    'resume': { 'es-ES': 'Reanuda la reproducción pausada', 'es-419': 'Reanuda la reproducción pausada', 'pt-BR': 'Retoma a reprodução pausada' },
    'volume': { 'es-ES': 'Ajusta el volumen de la música (0-100)', 'es-419': 'Ajusta el volumen de la música (0-100)', 'pt-BR': 'Ajusta o volume da música (0-100)' },
    'ban': { 'es-ES': 'Veta a un usuario del servidor', 'es-419': 'Veta a un usuario del servidor', 'pt-BR': 'Bane um usuário do servidor' },
    'kick': { 'es-ES': 'Expulsa a un usuario del servidor', 'es-419': 'Expulsa a un usuario del servidor', 'pt-BR': 'Expulsa um usuário do servidor' },
    'timeout': { 'es-ES': 'Aísla a un usuario (timeout)', 'es-419': 'Aísla a un usuario (timeout)', 'pt-BR': 'Isola um usuário (timeout)' },
    'warn': { 'es-ES': 'Advierte a un usuario y registra un incidente', 'es-419': 'Advierte a un usuario y registra un incidente', 'pt-BR': 'Adverte um usuário e registra um incidente' },
    'roll': { 'es-ES': 'Lanza un dado de N caras', 'es-419': 'Lanza un dado de N caras', 'pt-BR': 'Rola um dado de N lados' },
    'clear': { 'es-ES': 'Elimina una cantidad de mensajes del canal', 'es-419': 'Elimina una cantidad de mensajes del canal', 'pt-BR': 'Limpa uma quantidade de mensagens do canal' },
    'afk': { 'es-ES': 'Establece tu estado de ausencia (AFK)', 'es-419': 'Establece tu estado de ausencia (AFK)', 'pt-BR': 'Define seu estado de ausência (AFK)' },
    'poll': { 'es-ES': 'Crea una encuesta interactiva', 'es-419': 'Crea una encuesta interactiva', 'pt-BR': 'Cria uma enquete interativa' },
    'cat': { 'es-ES': 'Muestra una foto aleatoria de un gato', 'es-419': 'Muestra una foto aleatoria de un gato', 'pt-BR': 'Mostra uma foto aleatória de um gato' },
    'birthday': { 'es-ES': 'Registra tu fecha de cumpleaños', 'es-419': 'Registra tu fecha de cumpleaños', 'pt-BR': 'Registra sua data de aniversário' },
    'birthday-remove': { 'es-ES': 'Elimina tu fecha de cumpleaños', 'es-419': 'Elimina tu fecha de cumpleaños', 'pt-BR': 'Remove sua data de aniversário' },
    'welcome': { 'es-ES': 'Configura los mensajes de bienvenida', 'es-419': 'Configura los mensajes de bienvenida', 'pt-BR': 'Configura as mensagens de boas-vindas' },
    'lang': { 'es-ES': 'Cambia el idioma del bot en este servidor', 'es-419': 'Cambia el idioma del bot en este servidor', 'pt-BR': 'Muda o idioma do bot neste servidor' },
    'show': { 'es-ES': 'Muestra el resumen de ajustes del bot en este servidor', 'es-419': 'Muestra el resumen de ajustes del bot en este servidor', 'pt-BR': 'Mostra o resumo de ajustes do bot neste servidor' },
};

const commands = [];
for (const file of commandFiles) {
  const command = await import(path.join(commandsPath, file));
  if ('data' in command && 'execute' in command) {
    const json = command.data.toJSON();
    if (nameLocalizations[json.name]) {
        json.name_localizations = nameLocalizations[json.name];
    }
    if (descriptionLocalizations[json.name]) {
        json.description_localizations = descriptionLocalizations[json.name];
    }
    commands.push(json);
  }
}

const rest = new REST().setToken(process.env.DISCORD_TOKEN);

// 1. Wipe global commands to avoid duplicates
await rest.put(Routes.applicationCommands(process.env.DISCORD_CLIENT_ID), { body: [] });
console.log('[deploy] Comandos globales limpiados.');

// 2. Deploy ONLY as guild commands (instant update, no duplicates)
console.log('Fetching guilds...');
const client = new Client({ intents: [GatewayIntentBits.Guilds] });
await client.login(process.env.DISCORD_TOKEN);
const guilds = client.guilds.cache.map(g => g.id);

console.log('Deploying to guilds:', guilds);
for (const guildId of guilds) {
    try {
        await rest.put(Routes.applicationGuildCommands(process.env.DISCORD_CLIENT_ID, guildId), { body: commands });
        console.log(`[deploy] ${commands.length} comandos registrados en ${guildId}`);
    } catch (e) {
        console.error(`Error deploying to ${guildId}`, e);
    }
}
client.destroy();
console.log('[deploy] Listo.');
