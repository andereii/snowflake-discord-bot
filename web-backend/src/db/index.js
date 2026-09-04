// Conexión a la base de datos SQLite compartida con el bot C#.
// Lee la ruta desde .env o usa el default /app/data/snowflake.db
import Database from 'better-sqlite3';
import path from 'path';

const dbPath = process.env.DATABASE_PATH || path.join(process.cwd(), '..', 'data', 'snowflake.db');
const db = new Database(dbPath);
db.pragma('journal_mode = WAL');

export default db;
