import fs from 'fs';
import path from 'path';
import { pathToFileURL } from 'url';

function collectCommands(mod) {
    if (Array.isArray(mod.commands)) return mod.commands.filter(c => c?.data && c?.execute);
    if (mod.data && mod.execute) return [mod];
    return [];
}

async function walk(dir) {
    const commands = [];
    if (!fs.existsSync(dir)) return commands;

    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
            commands.push(...await walk(full));
            continue;
        }
        if (!entry.name.endsWith('.js') || entry.name.startsWith('_')) continue;
        const mod = await import(pathToFileURL(full).href);
        commands.push(...collectCommands(mod));
    }
    return commands;
}

export async function loadCommands(commandsDir) {
    const loaded = await walk(commandsDir);
    const seen = new Set();
    const unique = [];
    for (const cmd of loaded) {
        const name = cmd.data.name;
        if (seen.has(name)) {
            console.warn(`[bot] Duplicate command skipped: ${name}`);
            continue;
        }
        seen.add(name);
        unique.push(cmd);
    }
    return unique;
}
