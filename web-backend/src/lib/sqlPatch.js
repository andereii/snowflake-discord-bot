export function ensureRow(db, table, idColumn, id, insertSql) {
    const existing = db.prepare(`SELECT ${idColumn} FROM ${table} WHERE ${idColumn} = ?`).get(id);
    if (!existing) {
        db.prepare(insertSql || `INSERT INTO ${table} (${idColumn}) VALUES (?)`).run(id);
    }
}

export function applyPatch(db, table, idColumn, id, setters) {
    const updates = [];
    const values = [];
    for (const [column, value] of setters) {
        if (value === undefined) continue;
        updates.push(`${column} = ?`);
        values.push(value);
    }
    if (!updates.length) return false;
    values.push(id);
    db.prepare(`UPDATE ${table} SET ${updates.join(', ')} WHERE ${idColumn} = ?`).run(...values);
    return true;
}
