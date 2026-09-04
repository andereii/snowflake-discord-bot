import fs from 'fs';
import path from 'path';
import FormData from 'form-data';
import axios from 'axios';

const LITTERBOX_ENDPOINT = 'https://litterbox.catbox.moe/resources/internals/api.php';

/**
 * Upload a file to litterbox.catbox.moe (retained for 72 hours).
 * Used when a file exceeds Discord's max upload limit.
 * @param {string} filePath
 * @param {string} fileName
 * @returns {Promise<string>} Public temporary URL
 */
export async function uploadToLitterbox(filePath, fileName = null) {
    const name = fileName || path.basename(filePath);
    const form = new FormData();
    form.append('reqtype', 'fileupload');
    form.append('time', '72h');
    form.append('fileToUpload', fs.createReadStream(filePath), { filename: name });

    const response = await axios.post(LITTERBOX_ENDPOINT, form, {
        headers: form.getHeaders(),
        timeout: 120000
    });

    if (response.status !== 200) {
        throw new Error(`Litterbox responded with HTTP ${response.status}: ${response.data}`);
    }

    const url = String(response.data).trim();
    if (!url.startsWith('http')) {
        throw new Error(`Unexpected response from litterbox: ${url}`);
    }

    return url;
}

export default { uploadToLitterbox };
