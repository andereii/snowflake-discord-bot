// Proxy al bot C# para datos en tiempo real (miembros, roles, emojis, stats).
// El frontend React llama a Express; Express consulta al bot y devuelve.
import { Router } from 'express';
import { fetchGuildStats, fetchGuildMembers, fetchGuildRoles, fetchGuildEmojis, fetchGuildChannels } from '../services/botProxy.js';

const router = Router();

router.get('/:guildId/stats', async (req, res) => {
  const stats = await fetchGuildStats(req.params.guildId);
  if (!stats) return res.status(404).json({ error: 'Servidor no encontrado' });
  res.json(stats);
});

router.get('/:guildId/members', async (req, res) => {
  const members = await fetchGuildMembers(req.params.guildId);
  res.json({ members });
});

router.get('/:guildId/roles', async (req, res) => {
  const roles = await fetchGuildRoles(req.params.guildId);
  res.json({ roles });
});

router.get('/:guildId/emojis', async (req, res) => {
  const emojis = await fetchGuildEmojis(req.params.guildId);
  res.json({ emojis });
});

router.get('/:guildId/channels', async (req, res) => {
  const channels = await fetchGuildChannels(req.params.guildId);
  res.json({ channels });
});

export default router;
