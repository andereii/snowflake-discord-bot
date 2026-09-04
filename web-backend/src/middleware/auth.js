// API key guard: si está definida WEB_PANEL_API_KEY, toda mutación la exige.
export function apiKeyGuard(req, res, next) {
  const required = process.env.WEB_PANEL_API_KEY;
  if (!required) return next();
  const provided = req.headers['x-api-key'];
  if (provided !== required) {
    return res.status(401).json({ error: 'API key inválida o ausente' });
  }
  next();
}
