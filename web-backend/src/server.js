import 'dotenv/config';
import express from 'express';
import cors from 'cors';
import session from 'express-session';
import passport from 'passport';
import { Strategy as DiscordStrategy } from 'passport-discord';
import guildConfigRoutes from './routes/guildConfig.js';
import guildRoutes from './routes/guilds.js';
import authRoutes from './routes/auth.js';

const app = express();
const PORT = process.env.PORT || 3000;

app.use(cors({
  origin: process.env.FRONTEND_URL || 'http://localhost:5173',
  credentials: true
}));
app.use(express.json());

// Sesión (necesaria para passport).
app.use(session({
  secret: process.env.SESSION_SECRET || 'dev-secret-cambiar-en-prod',
  resave: false,
  saveUninitialized: false,
  cookie: { secure: process.env.NODE_ENV === 'production' }
}));

app.use(passport.initialize());
app.use(passport.session());

// Passport Discord Strategy.
passport.use(new DiscordStrategy({
  clientID: process.env.DISCORD_CLIENT_ID || '1052318909035970641',
  clientSecret: process.env.DISCORD_CLIENT_SECRET,
  callbackURL: process.env.DISCORD_CALLBACK_URL || `http://localhost:${PORT}/api/auth/discord/callback`,
  scope: ['identify', 'guilds']
}, (accessToken, refreshToken, profile, done) => {
  return done(null, { profile, accessToken });
}));

passport.serializeUser((user, done) => done(null, user));
passport.deserializeUser((user, done) => done(null, user));

// Rutas.
app.use('/api/auth', authRoutes);
app.use('/api/guilds', guildRoutes);
app.use('/api/guilds', guildConfigRoutes);

// Health check.
app.get('/api/health', (req, res) => res.json({ ok: true }));

app.listen(PORT, () => {
  console.log(`[web-backend] API escuchando en http://localhost:${PORT}`);
});
