// Conexión a la base de datos SQLite compartida.
import Database from 'better-sqlite3';
import path from 'path';
import 'dotenv/config';

const dbPath = process.env.DATABASE_PATH || path.join(process.cwd(), '..', 'data', 'snowflake.db');
const db = new Database(dbPath);
db.pragma('journal_mode = WAL');

export default db;
