import dotenv from 'dotenv';
import path from 'path';
import { fileURLToPath } from 'url';
import express from 'express';
import cors from 'cors';
import session from 'express-session';
import passport from 'passport';
import { Strategy as DiscordStrategy } from 'passport-discord';
import guildConfigRoutes from './routes/guildConfig.js';
import subConfigRoutes from './routes/subConfigs.js';
import guildRoutes from './routes/guilds.js';
import authRoutes from './routes/auth.js';
import { isLocalRuntime, runtimeName } from './lib/runtime.js';

const backendRoot = path.join(path.dirname(fileURLToPath(import.meta.url)), '..');
dotenv.config({ path: path.join(backendRoot, '..', '.env') });
dotenv.config({ path: path.join(backendRoot, '.env'), override: true });

const PORT = Number(process.env.PORT) || 3000;
const FRONTEND_URL = process.env.FRONTEND_URL || 'http://localhost:5173';
const CLIENT_ID = process.env.DISCORD_CLIENT_ID || '1052318909035970641';
const CLIENT_SECRET = process.env.DISCORD_CLIENT_SECRET;
const CALLBACK_URL = process.env.DISCORD_CALLBACK_URL || `http://localhost:${PORT}/api/auth/discord/callback`;

if (!CLIENT_SECRET) {
    console.error('[web-backend] DISCORD_CLIENT_SECRET is missing. Add it to web-backend/.env');
    process.exit(1);
}

const local = isLocalRuntime();
const app = express();

const corsOrigins = local
    ? [FRONTEND_URL, 'http://localhost:5173', 'http://127.0.0.1:5173']
    : [FRONTEND_URL];

app.use(cors({
    origin: corsOrigins,
    credentials: true
}));
app.use(express.json());

app.use(session({
    secret: process.env.SESSION_SECRET || 'dev-secret-change-in-prod',
    resave: false,
    saveUninitialized: false,
    cookie: {
        httpOnly: true,
        sameSite: 'lax',
        secure: !local
    }
}));

app.use(passport.initialize());
app.use(passport.session());

passport.use(new DiscordStrategy({
    clientID: CLIENT_ID,
    clientSecret: CLIENT_SECRET,
    callbackURL: CALLBACK_URL,
    scope: ['identify', 'guilds']
}, (accessToken, refreshToken, profile, done) => {
    return done(null, { profile, accessToken });
}));

passport.serializeUser((user, done) => done(null, user));
passport.deserializeUser((user, done) => done(null, user));

app.use('/api/auth', authRoutes);
app.use('/api/guilds', guildRoutes);
app.use('/api/guilds', guildConfigRoutes);
app.use('/api/guilds', subConfigRoutes);

app.get('/api/health', (req, res) => res.json({ ok: true }));

app.use((err, req, res, next) => {
    console.error('[web-backend]', err);
    if (res.headersSent) return next(err);
    res.status(500).json({ error: err.message || 'Internal server error' });
});

const bindHost = process.env.BIND_HOST || (local ? '127.0.0.1' : '0.0.0.0');
app.listen(PORT, bindHost, () => {
    console.log(`[web-backend] runtime: ${runtimeName()}`);
    console.log(`[web-backend] listening on http://${bindHost}:${PORT}`);
    console.log(`[web-backend] OAuth callback: ${CALLBACK_URL}`);
});
