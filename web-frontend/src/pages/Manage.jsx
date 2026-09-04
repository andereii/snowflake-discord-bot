import { useParams } from 'react-router-dom';
import { useEffect, useState } from 'react';
import axios from 'axios';

export default function Manage() {
  const { guildId } = useParams();
  const [config, setConfig] = useState(null);
  const [activeSection, setActiveSection] = useState('moderation');

  useEffect(() => {
    axios.get(`/api/guilds/${guildId}/config`)
      .then(res => setConfig(res.data))
      .catch(console.error);
  }, [guildId]);

  const handleSave = async (payload, endpoint = '') => {
    try {
      await axios.post(`/api/guilds/${guildId}/config${endpoint}`, payload, {
        headers: { 'X-Api-Key': localStorage.getItem('snowflake_panel_api_key') || '' }
      });
      const res = await axios.get(`/api/guilds/${guildId}/config`);
      setConfig(res.data);
      alert('Guardado con éxito');
    } catch (err) {
      if (err.response?.status === 401) alert('Clave API incorrecta');
      else alert('Error al guardar');
    }
  };

  if (!config) return <div>Cargando configuración...</div>;

  return (
    <div className="manage-page">
      <aside className="sidebar">
        <nav>
          <button onClick={() => setActiveSection('moderation')} className={activeSection === 'moderation' ? 'active' : ''}>Moderación</button>
          <button onClick={() => setActiveSection('welcome')} className={activeSection === 'welcome' ? 'active' : ''}>Bienvenida</button>
          <button onClick={() => setActiveSection('music')} className={activeSection === 'music' ? 'active' : ''}>Música</button>
          <button onClick={() => setActiveSection('ai')} className={activeSection === 'ai' ? 'active' : ''}>IA</button>
          <button onClick={() => setActiveSection('counting')} className={activeSection === 'counting' ? 'active' : ''}>Conteo</button>
          <button onClick={() => setActiveSection('youtube')} className={activeSection === 'youtube' ? 'active' : ''}>YouTube</button>
          <button onClick={() => setActiveSection('voice')} className={activeSection === 'voice' ? 'active' : ''}>Canales de Voz</button>
          <button onClick={() => setActiveSection('downloads')} className={activeSection === 'downloads' ? 'active' : ''}>Descargas</button>
        </nav>
      </aside>

      <main className="manage-content">
        {activeSection === 'moderation' && <ModerationSection config={config} onSave={handleSave} />}
        {activeSection === 'welcome' && <WelcomeSection config={config} onSave={handleSave} />}
        {activeSection === 'music' && <MusicSection config={config} onSave={handleSave} />}
        {activeSection === 'ai' && <AISection config={config} onSave={handleSave} />}
        {activeSection === 'counting' && <CountingSection config={config} onSave={handleSave} />}
        {activeSection === 'youtube' && <YouTubeSection config={config} onSave={handleSave} />}
        {activeSection === 'voice' && <VoiceSection config={config} onSave={handleSave} />}
        {activeSection === 'downloads' && <DownloadsSection config={config} onSave={handleSave} />}
      </main>
    </div>
  );
}

function ModerationSection({ config, onSave }) {
  const [logChannelId, setLogChannelId] = useState(config.moderation?.logChannelId || '');
  return (
    <section>
      <h2>Moderación</h2>
      <input type="text" value={logChannelId} onChange={e => setLogChannelId(e.target.value)} placeholder="ID del canal de logs" />
      <button onClick={() => onSave({ modLogChannelId: logChannelId })}>Guardar</button>
    </section>
  );
}

function WelcomeSection({ config, onSave }) {
  const [channelId, setChannelId] = useState(config.welcome?.channelId || '');
  const [message, setMessage] = useState(config.welcome?.message || '');
  return (
    <section>
      <h2>Bienvenida</h2>
      <input type="text" value={channelId} onChange={e => setChannelId(e.target.value)} placeholder="ID del canal de bienvenida" />
      <textarea value={message} onChange={e => setMessage(e.target.value)} placeholder="Mensaje de bienvenida"></textarea>
      <button onClick={() => onSave({ welcomeChannelId: channelId, welcomeMessage: message })}>Guardar</button>
    </section>
  );
}

function MusicSection({ config, onSave }) {
  const [djRoleId, setDjRoleId] = useState(config.music?.djRoleId || '');
  const [volume, setVolume] = useState(config.music?.volume ?? 100);
  return (
    <section>
      <h2>Música</h2>
      <input type="text" value={djRoleId} onChange={e => setDjRoleId(e.target.value)} placeholder="ID del rol DJ" />
      <input type="number" value={volume} onChange={e => setVolume(Number(e.target.value))} placeholder="Volumen (0-100)" />
      <button onClick={() => onSave({ djRoleId, volume })}>Guardar</button>
    </section>
  );
}

function AISection({ config, onSave }) {
  const [chatEnabled, setChatEnabled] = useState(config.ai?.chatEnabled ?? true);
  return (
    <section>
      <h2>Inteligencia Artificial</h2>
      <label>
        <input type="checkbox" checked={chatEnabled} onChange={e => setChatEnabled(e.target.checked)} />
        Chat con IA
      </label>
      <button onClick={() => onSave({ aiChatEnabled: chatEnabled })}>Guardar</button>
    </section>
  );
}

function CountingSection({ config, onSave }) {
  const [channelId, setChannelId] = useState(config.counting?.channelId || '');
  return (
    <section>
      <h2>Conteo</h2>
      <input type="text" value={channelId} onChange={e => setChannelId(e.target.value)} placeholder="ID del canal de conteo" />
      <button onClick={() => onSave({ channelId }, '/counting')}>Guardar</button>
    </section>
  );
}

function YouTubeSection({ config, onSave }) {
  const [ytChannelId, setYtChannelId] = useState(config.youtube?.channelId || '');
  const [notifyChannelId, setNotifyChannelId] = useState(config.youtube?.notifyChannelId || '');
  return (
    <section>
      <h2>YouTube</h2>
      <input type="text" value={ytChannelId} onChange={e => setYtChannelId(e.target.value)} placeholder="ID del canal de YouTube" />
      <input type="text" value={notifyChannelId} onChange={e => setNotifyChannelId(e.target.value)} placeholder="ID del canal de notificaciones" />
      <button onClick={() => onSave({ ytChannelId, notifyChannelId }, '/youtube')}>Guardar</button>
    </section>
  );
}

function VoiceSection({ config, onSave }) {
  const [hubChannelId, setHubChannelId] = useState(config.voice?.hubChannelId || '');
  const [tempChannelNameTemplate, setTempChannelNameTemplate] = useState(config.voice?.tempChannelNameTemplate || '');
  return (
    <section>
      <h2>Canales de Voz</h2>
      <input type="text" value={hubChannelId} onChange={e => setHubChannelId(e.target.value)} placeholder="ID del canal HUB" />
      <input type="text" value={tempChannelNameTemplate} onChange={e => setTempChannelNameTemplate(e.target.value)} placeholder="Plantilla de nombre (ej. Canal de {user})" />
      <button onClick={() => onSave({ hubChannelId, tempChannelNameTemplate })}>Guardar</button>
    </section>
  );
}

function DownloadsSection({ config, onSave }) {
  const [downloadsEnabled, setDownloadsEnabled] = useState(config.downloads?.enabled ?? true);
  return (
    <section>
      <h2>Descargas</h2>
      <label>
        <input type="checkbox" checked={downloadsEnabled} onChange={e => setDownloadsEnabled(e.target.checked)} />
        Descargas habilitadas
      </label>
      <button onClick={() => onSave({ downloadsEnabled })}>Guardar</button>
    </section>
  );
}
