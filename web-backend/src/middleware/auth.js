export function apiKeyGuard(req, res, next) {
    const required = process.env.WEB_PANEL_API_KEY;
    if (!required) return next();
    if (req.headers['x-api-key'] !== required) {
        return res.status(401).json({ error: 'Invalid or missing API key' });
    }
    next();
}
