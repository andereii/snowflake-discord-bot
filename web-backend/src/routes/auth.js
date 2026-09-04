import { Router } from 'express';
import passport from 'passport';

const router = Router();

// GET /api/auth/discord — inicia el flujo OAuth2 de Discord.
router.get('/discord', passport.authenticate('discord'));

// GET /api/auth/discord/callback — Discord redirige aquí con el código.
router.get('/discord/callback', passport.authenticate('discord', {
  failureRedirect: '/'
}), (req, res) => {
  // Redirige al frontend con el token en la sesión.
  const redirect = process.env.FRONTEND_URL || 'http://localhost:5173';
  res.redirect(`${redirect}?login=success`);
});

// GET /api/auth/me — devuelve el usuario autenticado (requiere sesión).
router.get('/me', (req, res) => {
  if (!req.isAuthenticated()) {
    return res.status(401).json({ error: 'No autenticado' });
  }
  res.json({ user: req.user.profile });
});

// GET /api/auth/guilds — servidores del usuario donde es admin + si el bot está.
router.get('/guilds', async (req, res) => {
  if (!req.isAuthenticated()) {
    return res.status(401).json({ error: 'No autenticado' });
  }

  const accessToken = req.user.accessToken;

  try {
    const response = await fetch('https://discord.com/api/users/@me/guilds', {
      headers: { Authorization: `Bearer ${accessToken}` }
    });
    const guilds = await response.json();

    // Filtrar solo donde tiene Administrator.
    const adminGuilds = guilds.filter(g => (BigInt(g.permissions) & 8n) === 8n);
    res.json({ guilds: adminGuilds });
  } catch (error) {
    res.status(500).json({ error: 'Error obteniendo servidores' });
  }
});

// POST /api/auth/logout
router.post('/logout', (req, res) => {
  req.logout(() => {
    res.json({ ok: true });
  });
});

export default router;
