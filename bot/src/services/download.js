import { spawn } from 'child_process';
import path from 'path';
import fs from 'fs';
import os from 'os';
import crypto from 'crypto';

/**
 * Custom error for yt-dlp failures
 */
export class YtDlpException extends Error {
    constructor(message) {
        super(message);
        this.name = 'YtDlpException';
    }
}

/**
 * Downloads media from any URL supported by yt-dlp.
 * @param {string} url
 * @param {boolean} [audioOnly=false]
 * @param {number} [timeoutMinutes=4]
 * @returns {Promise<{ filePath: string, title: string, tempDir: string }>}
 */
export async function downloadMedia(url, audioOnly = false, timeoutMinutes = 4) {
    const tempDir = path.join(os.tmpdir(), 'snowflake_dl', crypto.randomUUID().replace(/-/g, ''));
    fs.mkdirSync(tempDir, { recursive: true });

    const template = path.join(tempDir, '%(title).80B [%(id)s].%(ext)s');

    const args = [
        '--no-playlist',
        '--no-progress',
        '--no-warnings',
        '--no-part',
        '--restrict-filenames',
        '--print',
        'after_move:filepath',
        '-o',
        template
    ];

    const cookiesFile = process.env.YT_COOKIES_FILE;
    if (cookiesFile && fs.existsSync(cookiesFile)) {
        args.push('--cookies', cookiesFile);
    }

    if (audioOnly) {
        args.push('-x', '--audio-format', 'mp3', '--audio-quality', '0');
    }

    args.push(url);

    return new Promise((resolve, reject) => {
        const proc = spawn('yt-dlp', args);

        let stdout = '';
        let stderr = '';

        proc.stdout.on('data', chunk => { stdout += chunk.toString(); });
        proc.stderr.on('data', chunk => { stderr += chunk.toString(); });

        const timeoutMs = (Math.max(1, timeoutMinutes) + 1) * 60 * 1000;
        const timer = setTimeout(() => {
            try { proc.kill('SIGKILL'); } catch {}
            cleanupDir(tempDir);
            reject(new YtDlpException('La descarga tardó demasiado y fue cancelada.'));
        }, timeoutMs);

        proc.on('close', code => {
            clearTimeout(timer);

            if (code !== 0) {
                cleanupDir(tempDir);
                const details = stderr.trim() || stdout.trim() || `Código de salida ${code}`;
                const sanitized = sanitizeError(details);
                return reject(new YtDlpException(sanitized));
            }

            const lines = stdout.split('\n').map(l => l.trim()).filter(l => l.length > 0);
            let filePath = lines[lines.length - 1];

            if (!filePath || !fs.existsSync(filePath)) {
                const files = fs.readdirSync(tempDir);
                if (files.length === 0) {
                    cleanupDir(tempDir);
                    return reject(new YtDlpException('No se generó ningún archivo tras la descarga.'));
                }
                filePath = path.join(tempDir, files[0]);
            }

            const title = path.basename(filePath, path.extname(filePath));
            resolve({ filePath, title, tempDir });
        });

        proc.on('error', err => {
            clearTimeout(timer);
            cleanupDir(tempDir);
            reject(new YtDlpException(`Error al ejecutar yt-dlp: ${err.message}`));
        });
    });
}

function cleanupDir(dirPath) {
    try {
        if (dirPath && fs.existsSync(dirPath)) {
            fs.rmSync(dirPath, { recursive: true, force: true });
        }
    } catch {}
}

function sanitizeError(msg) {
    if (!msg) return 'Error desconocido de yt-dlp.';
    const lines = msg.split('\n').map(l => l.trim()).filter(l => l.length > 0);
    const summary = lines.slice(-3).join(' | ');
    return summary.length > 800 ? summary.substring(0, 800) + '…' : summary;
}

export default { downloadMedia, YtDlpException };
