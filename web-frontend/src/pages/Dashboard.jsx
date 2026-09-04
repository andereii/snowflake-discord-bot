import { useEffect, useState } from 'react';
import axios from 'axios';

export default function Dashboard() {
  const [user, setUser] = useState(null);
  const [guilds, setGuilds] = useState([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    axios.get('/api/auth/me', { withCredentials: true })
      .then(res => {
        setUser(res.data.user);
        return axios.get('/api/auth/guilds', { withCredentials: true });
      })
      .then(res => {
        setGuilds(res.data.guilds || []);
      })
      .catch(() => {
        // No autenticado, el usuario debe logearse.
      })
      .finally(() => setLoading(false));
  }, []);

  if (loading) return <div>Cargando...</div>;

  if (!user) {
    return (
      <div>
        <h1>Dashboard - Snowflake</h1>
        <a href="/api/auth/discord" className="btn-discord">
          Iniciar sesión con Discord
        </a>
      </div>
    );
  }

  return (
    <div className="dashboard">
      <header>
        <h1>Bienvenido, {user.username}</h1>
        <button onClick={() => axios.post('/api/auth/logout', {}, { withCredentials: true }).then(() => window.location.reload())}>
          Cerrar sesión
        </button>
      </header>

      <section>
        <h2>Tus servidores</h2>
        <div className="servers-grid">
          {guilds.map(guild => (
            <div key={guild.id} className="server-card">
              <img
                src={guild.icon ? `https://cdn.discordapp.com/icons/${guild.id}/${guild.icon}.png` : '/placeholder.png'}
                alt=""
                className="server-icon"
              />
              <h3>{guild.name}</h3>
              <a href={`/manage/${guild.id}`} className="btn-manage">Configurar</a>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}
