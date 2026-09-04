import { API_BASE } from '../auth/config.js';

// Lista de emojis unicode comunes para el selector.
const UNICODE_EMOJIS = [
    '😀','😁','😂','🤣','😊','😍','😘','😎','🤩','🥳',
    '😭','😢','😡','🤔','🤯','😱','🙄','😴','','🤝',
    '👍','👎','','🙌','','💪','','❤️','','💛',
    '💚','💙','💜','🖤','🤍','💔','❣️','💕','💞','💓',
    '🔥','✨','⭐','🌟','💫','','🎉','','🎈','🎁',
    '🏆','','🎯','','🎵','','🍕','','☕','🍰'
];

/**
 * Generador de mensajes embed con vista previa en vivo.
 * Los botones de acción insertan variables, emojis, menciones de canal y
 * menciones de rol/usuario en el campo de texto activo, en el formato que
 * Discord entiende ({{var}}, <:name:id>, <#id>, <@id>, <@&id>).
 *
 * options:
 *  - guildId: servidor del que se leen emojis/canales/roles/miembros.
 *  - context: 'welcome' | 'leave' | 'birthday' | 'general'. Cambia la
 *    descripción de las variables según el apartado donde se use.
 */
export class EmbedGenerator {
    constructor(container, options = {}) {
        this.container = typeof container === 'string' ? document.querySelector(container) : container;
        this.options = {
            guildId: options.guildId ?? null,
            context: options.context ?? 'general',
            ...options
        };

        this.state = {
            author: '',
            authorIcon: null,
            title: '',
            description: '',
            color: '#ff4d8b',
            fields: [],
            image: null,
            footer: '',
            footerIcon: null
        };

        // Caché de datos del servidor (cargados bajo demanda).
        this.cache = { emojis: null, channels: null, roles: null, members: null };

        // Último campo de texto enfocado (para insertar en el cursor).
        this.activeInput = null;

        this.render();
        this.attachEventListeners();
        this.updatePreview();
    }

    // ------------------------------------------------------------------
    // Variables disponibles según el contexto
    // ------------------------------------------------------------------

    getVariables() {
        const ctx = this.options.context;
        const userDesc = {
            welcome: 'Menciona al miembro que ingresó al servidor.',
            leave: 'Menciona al miembro que ha dejado el servidor.',
            birthday: 'Menciona al miembro que cumple años.',
            general: 'Menciona a un miembro del servidor.'
        }[ctx] ?? 'Menciona a un miembro del servidor.';

        const usernameDesc = {
            welcome: 'Nombre del miembro que ingresó (sin formato de mención).',
            leave: 'Nombre del miembro que se fue (sin formato de mención).',
            birthday: 'Nombre del miembro que cumple años (sin formato de mención).',
            general: 'Nombre de un miembro (sin formato de mención).'
        }[ctx] ?? 'Nombre de un miembro (sin formato de mención).';

        const useridDesc = {
            welcome: 'ID del miembro que ingresó.',
            leave: 'ID del miembro que se fue.',
            birthday: 'ID del miembro que cumple años.',
            general: 'ID de un miembro.'
        }[ctx] ?? 'ID de un miembro.';

        return [
            { token: '{{user}}', description: userDesc },
            { token: '{{username}}', description: usernameDesc },
            { token: '{{userid}}', description: useridDesc },
            { token: '{{server}}', description: 'Nombre del servidor.' },
            { token: '{{boost}}', description: 'Cantidad de mejoras (boosts) del servidor.' },
            { token: '{{members}}', description: 'Cantidad total de miembros del servidor.' },
            { token: '{{date}}', description: 'Fecha actual en timestamp (formato según el idioma del servidor).' }
        ];
    }

    // ------------------------------------------------------------------
    // Render del esqueleto
    // ------------------------------------------------------------------

    render() {
        this.container.innerHTML = `
            <div class="embed-generator">
                <div class="embed-generator-preview">
                    <div class="embed-generator-preview-embed">
                        <div class="embed-generator-preview-author" style="display:none;">
                            <img class="embed-generator-preview-author-icon" src="" alt="" style="display:none;">
                            <span class="embed-generator-preview-author-name"></span>
                        </div>
                        <div class="embed-generator-preview-title" style="display:none;"></div>
                        <div class="embed-generator-preview-description" style="display:none;"></div>
                        <div class="embed-generator-preview-fields"></div>
                        <img class="embed-generator-preview-image" src="" alt="" style="display:none;">
                        <div class="embed-generator-preview-footer" style="display:none;">
                            <img class="embed-generator-preview-footer-icon" src="" alt="" style="display:none;">
                            <span class="embed-generator-preview-footer-text"></span>
                        </div>
                    </div>
                </div>

                <div class="embed-generator-section">
                    <div class="embed-generator-section-title">Autor</div>
                    <input type="text" class="embed-generator-input" id="eg-author" placeholder="Nombre del autor">
                    <input type="text" class="embed-generator-input" id="eg-author-icon" placeholder="URL del icono del autor (opcional)">
                </div>

                <div class="embed-generator-section">
                    <div class="embed-generator-section-title">Título</div>
                    <input type="text" class="embed-generator-input" id="eg-title" placeholder="Título del embed">
                </div>

                <div class="embed-generator-section">
                    <div class="embed-generator-section-title">Mensaje</div>
                    <textarea class="embed-generator-textarea" id="eg-description" placeholder="Escribe tu mensaje aquí..."></textarea>
                    <div class="embed-generator-actions">
                        <button class="embed-generator-action-btn" id="eg-emoji-btn" title="Insertar emoji o icono">😊</button>
                        <button class="embed-generator-action-btn" id="eg-channel-btn" title="Mencionar canal">#</button>
                        <button class="embed-generator-action-btn" id="eg-mention-btn" title="Mencionar persona o rol">🛡️</button>
                        <button class="embed-generator-action-btn" id="eg-variables-btn" title="Insertar variable">{}</button>
                    </div>
                </div>

                <div class="embed-generator-section">
                    <div class="embed-generator-section-title">Color del borde</div>
                    <input type="color" id="eg-color" value="${this.state.color}" style="width:60px;height:36px;border:none;background:none;cursor:pointer;">
                </div>

                <div class="embed-generator-section">
                    <button class="embed-generator-add-field" id="eg-add-field">
                        <span>+</span> Añadir campo
                    </button>
                    <div id="eg-fields-container"></div>
                </div>

                <div class="embed-generator-section">
                    <div class="embed-generator-section-title">Imagen del embed</div>
                    <label class="embed-generator-upload">
                        <div class="embed-generator-upload-icon">🖼️</div>
                        <div class="embed-generator-upload-text">Imagen del embed</div>
                        <div class="embed-generator-upload-hint">Clic para subir</div>
                        <input type="file" id="eg-image" accept="image/*" style="display:none;">
                    </label>
                </div>

                <div class="embed-generator-section">
                    <div class="embed-generator-section-title">Pie de página</div>
                    <input type="text" class="embed-generator-input" id="eg-footer" placeholder="Texto del pie de página">
                    <input type="text" class="embed-generator-input" id="eg-footer-icon" placeholder="URL del icono del pie (opcional)">
                </div>
            </div>
        `;
    }

    // ------------------------------------------------------------------
    // Eventos
    // ------------------------------------------------------------------

    attachEventListeners() {
        const q = (sel) => this.container.querySelector(sel);

        // Track del campo activo para insertar en el cursor.
        ['#eg-author', '#eg-title', '#eg-description', '#eg-footer'].forEach(sel => {
            const el = q(sel);
            el.addEventListener('focus', () => { this.activeInput = el; });
            el.addEventListener('input', () => this.syncStateFromInputs());
        });

        ['#eg-author-icon', '#eg-footer-icon'].forEach(sel => {
            q(sel).addEventListener('input', () => this.syncStateFromInputs());
        });

        q('#eg-color').addEventListener('input', (e) => {
            this.state.color = e.target.value;
            this.updatePreview();
        });

        // Botones de acción.
        q('#eg-variables-btn').addEventListener('click', (e) => { e.stopPropagation(); this.showVariablesPopup(e.target); });
        q('#eg-emoji-btn').addEventListener('click', (e) => { e.stopPropagation(); this.showEmojiPopup(e.target); });
        q('#eg-channel-btn').addEventListener('click', (e) => { e.stopPropagation(); this.showChannelPopup(e.target); });
        q('#eg-mention-btn').addEventListener('click', (e) => { e.stopPropagation(); this.showMentionPopup(e.target); });

        // Campos.
        q('#eg-add-field').addEventListener('click', () => this.addField());

        // Imagen.
        q('#eg-image').addEventListener('change', (e) => {
            const file = e.target.files[0];
            if (!file) return;
            const reader = new FileReader();
            reader.onload = (ev) => { this.state.image = ev.target.result; this.updatePreview(); };
            reader.readAsDataURL(file);
        });

        document.addEventListener('click', () => this.closeAllPopups());
    }

    syncStateFromInputs() {
        const q = (sel) => this.container.querySelector(sel);
        this.state.author = q('#eg-author').value;
        this.state.authorIcon = q('#eg-author-icon').value || null;
        this.state.title = q('#eg-title').value;
        this.state.description = q('#eg-description').value;
        this.state.footer = q('#eg-footer').value;
        this.state.footerIcon = q('#eg-footer-icon').value || null;
        this.updatePreview();
    }

    // ------------------------------------------------------------------
    // Campos dinámicos
    // ------------------------------------------------------------------

    addField() {
        this.state.fields.push({ name: '', value: '', inline: false });
        this.renderFields();
        this.updatePreview();
    }

    removeField(index) {
        this.state.fields.splice(index, 1);
        this.renderFields();
        this.updatePreview();
    }

    renderFields() {
        const container = this.container.querySelector('#eg-fields-container');
        container.innerHTML = '';
        this.state.fields.forEach((field, i) => {
            const div = document.createElement('div');
            div.className = 'embed-generator-field';
            div.innerHTML = `
                <div class="embed-generator-field-header">
                    <span style="font-size:13px;color:var(--color-text-muted);">Campo ${i + 1}</span>
                    <button class="embed-generator-field-remove" title="Quitar campo">✕</button>
                </div>
                <div class="embed-generator-field-row">
                    <input type="text" class="embed-generator-field-input" data-field="name" placeholder="Nombre del campo" value="${field.name}">
                </div>
                <div class="embed-generator-field-row">
                    <input type="text" class="embed-generator-field-input" data-field="value" placeholder="Valor del campo" value="${field.value}">
                </div>
                <label class="embed-generator-field-checkbox">
                    <input type="checkbox" data-field="inline" ${field.inline ? 'checked' : ''}> En línea (inline)
                </label>
            `;
            div.querySelector('.embed-generator-field-remove').addEventListener('click', () => this.removeField(i));
            div.querySelectorAll('.embed-generator-field-input, .embed-generator-field-checkbox input').forEach(input => {
                input.addEventListener('input', () => {
                    field.name = div.querySelector('[data-field="name"]').value;
                    field.value = div.querySelector('[data-field="value"]').value;
                    field.inline = div.querySelector('[data-field="inline"]').checked;
                    this.updatePreview();
                });
            });
            container.appendChild(div);
        });
    }

    // ------------------------------------------------------------------
    // Preview
    // ------------------------------------------------------------------

    updatePreview() {
        const q = (sel) => this.container.querySelector(sel);
        const embed = q('.embed-generator-preview-embed');
        embed.style.borderLeftColor = this.state.color;

        const author = q('.embed-generator-preview-author');
        const authorIcon = q('.embed-generator-preview-author-icon');
        const authorName = q('.embed-generator-preview-author-name');
        if (this.state.author) {
            author.style.display = 'flex';
            authorName.textContent = this.state.author;
            if (this.state.authorIcon) { authorIcon.style.display = 'block'; authorIcon.src = this.state.authorIcon; }
            else authorIcon.style.display = 'none';
        } else author.style.display = 'none';

        const title = q('.embed-generator-preview-title');
        if (this.state.title) { title.style.display = 'block'; title.textContent = this.state.title; }
        else title.style.display = 'none';

        const desc = q('.embed-generator-preview-description');
        if (this.state.description) { desc.style.display = 'block'; desc.textContent = this.state.description; }
        else desc.style.display = 'none';

        const fieldsBox = q('.embed-generator-preview-fields');
        fieldsBox.innerHTML = '';
        this.state.fields.filter(f => f.name || f.value).forEach(f => {
            const div = document.createElement('div');
            div.className = 'embed-generator-preview-field';
            if (f.inline) div.style.gridColumn = 'span 1';
            div.innerHTML = `<div class="embed-generator-preview-field-name"></div><div class="embed-generator-preview-field-value"></div>`;
            div.querySelector('.embed-generator-preview-field-name').textContent = f.name;
            div.querySelector('.embed-generator-preview-field-value').textContent = f.value;
            fieldsBox.appendChild(div);
        });

        const img = q('.embed-generator-preview-image');
        if (this.state.image) { img.style.display = 'block'; img.src = this.state.image; }
        else img.style.display = 'none';

        const footer = q('.embed-generator-preview-footer');
        const footerIcon = q('.embed-generator-preview-footer-icon');
        const footerText = q('.embed-generator-preview-footer-text');
        if (this.state.footer) {
            footer.style.display = 'flex';
            footerText.textContent = this.state.footer;
            if (this.state.footerIcon) { footerIcon.style.display = 'block'; footerIcon.src = this.state.footerIcon; }
            else footerIcon.style.display = 'none';
        } else footer.style.display = 'none';
    }

    // ------------------------------------------------------------------
    // Inserción en el cursor
    // ------------------------------------------------------------------

    insertAtCursor(text) {
        const el = this.activeInput || this.container.querySelector('#eg-description');
        const start = el.selectionStart ?? el.value.length;
        const end = el.selectionEnd ?? el.value.length;
        el.value = el.value.slice(0, start) + text + el.value.slice(end);
        el.focus();
        el.selectionStart = el.selectionEnd = start + text.length;
        this.syncStateFromInputs();
    }

    // ------------------------------------------------------------------
    // Popups
    // ------------------------------------------------------------------

    closeAllPopups() {
        document.querySelectorAll('.embed-generator-popup').forEach(p => p.remove());
    }

    openPopup(targetElement, innerHTML) {
        this.closeAllPopups();
        const popup = document.createElement('div');
        popup.className = 'embed-generator-popup';
        const rect = targetElement.getBoundingClientRect();
        popup.style.top = `${rect.bottom + window.scrollY + 8}px`;
        popup.style.left = `${rect.left + window.scrollX}px`;
        popup.innerHTML = innerHTML;
        document.body.appendChild(popup);
        return popup;
    }

    showVariablesPopup(target) {
        const popup = this.openPopup(target, `
            <ul class="embed-generator-popup-list">
                ${this.getVariables().map(v => `
                    <li class="embed-generator-popup-item" data-token="${v.token}" title="${v.description}">
                        <code>${v.token}</code>
                    </li>
                `).join('')}
            </ul>
        `);
        popup.querySelectorAll('.embed-generator-popup-item').forEach(item => {
            item.addEventListener('click', () => {
                this.insertAtCursor(item.dataset.token);
                this.closeAllPopups();
            });
        });
    }

    showEmojiPopup(target) {
        const popup = this.openPopup(target, `
            <input type="text" class="embed-generator-popup-search" placeholder="Buscar emoji...">
            <ul class="embed-generator-popup-list"></ul>
        `);
        const list = popup.querySelector('.embed-generator-popup-list');
        const search = popup.querySelector('.embed-generator-popup-search');

        const renderList = (filter) => {
            list.innerHTML = '';
            // Emojis unicode.
            UNICODE_EMOJIS.filter(e => !filter || e.includes(filter)).forEach(e => {
                const li = document.createElement('li');
                li.className = 'embed-generator-popup-item';
                li.innerHTML = `<span class="embed-generator-popup-emoji">${e}</span>`;
                li.addEventListener('click', () => { this.insertAtCursor(e); this.closeAllPopups(); });
                list.appendChild(li);
            });
            // Emojis custom del servidor.
            (this.cache.emojis || []).forEach(e => {
                if (filter && !e.name.toLowerCase().includes(filter.toLowerCase())) return;
                const li = document.createElement('li');
                li.className = 'embed-generator-popup-item';
                li.innerHTML = `<img class="embed-generator-popup-emoji-custom" src="${e.url}" alt=""><span>${e.name}</span>`;
                li.addEventListener('click', () => {
                    this.insertAtCursor(e.animated ? `<a:${e.name}:${e.id}>` : `<:${e.name}:${e.id}>`);
                    this.closeAllPopups();
                });
                list.appendChild(li);
            });
        };

        this.fetchServerData('emojis').then(() => renderList(''));
        search.addEventListener('input', () => renderList(search.value));
    }

    showChannelPopup(target) {
        const popup = this.openPopup(target, `
            <input type="text" class="embed-generator-popup-search" placeholder="Buscar canal...">
            <ul class="embed-generator-popup-list"></ul>
        `);
        const list = popup.querySelector('.embed-generator-popup-list');
        const search = popup.querySelector('.embed-generator-popup-search');

        const renderList = (filter) => {
            list.innerHTML = '';
            (this.cache.channels || [])
                .filter(c => !filter || c.name.toLowerCase().includes(filter.toLowerCase()))
                .forEach(c => {
                    const li = document.createElement('li');
                    li.className = 'embed-generator-popup-item';
                    li.innerHTML = `<span>#</span><span>${c.name}</span>`;
                    li.addEventListener('click', () => { this.insertAtCursor(`<#${c.id}>`); this.closeAllPopups(); });
                    list.appendChild(li);
                });
        };

        this.fetchServerData('channels').then(() => renderList(''));
        search.addEventListener('input', () => renderList(search.value));
    }

    showMentionPopup(target) {
        const popup = this.openPopup(target, `
            <input type="text" class="embed-generator-popup-search" placeholder="Buscar rol o miembro...">
            <ul class="embed-generator-popup-list"></ul>
        `);
        const list = popup.querySelector('.embed-generator-popup-list');
        const search = popup.querySelector('.embed-generator-popup-search');

        const renderList = (filter) => {
            list.innerHTML = '';
            (this.cache.roles || [])
                .filter(r => !filter || r.name.toLowerCase().includes(filter.toLowerCase()))
                .forEach(r => {
                    const li = document.createElement('li');
                    li.className = 'embed-generator-popup-item';
                    li.innerHTML = `<span style="color:#${r.color};">●</span><span>@${r.name}</span>`;
                    li.addEventListener('click', () => { this.insertAtCursor(`<@&${r.id}>`); this.closeAllPopups(); });
                    list.appendChild(li);
                });
            (this.cache.members || [])
                .filter(m => !filter || (m.displayName || m.username).toLowerCase().includes(filter.toLowerCase()))
                .forEach(m => {
                    const li = document.createElement('li');
                    li.className = 'embed-generator-popup-item';
                    li.innerHTML = `<img class="embed-generator-popup-emoji-custom" src="${m.avatarUrl || 'https://cdn.discordapp.com/embed/avatars/0.png'}" alt=""><span>${m.displayName || m.username}</span>`;
                    li.addEventListener('click', () => { this.insertAtCursor(`<@${m.id}>`); this.closeAllPopups(); });
                    list.appendChild(li);
                });
        };

        Promise.all([this.fetchServerData('roles'), this.fetchServerData('members')]).then(() => renderList(''));
        search.addEventListener('input', () => renderList(search.value));
    }

    // ------------------------------------------------------------------
    // Datos del servidor (lazy + cache)
    // ------------------------------------------------------------------

    async fetchServerData(kind) {
        if (this.cache[kind]) return this.cache[kind];
        if (!this.options.guildId) return null;
        try {
            const res = await fetch(`${API_BASE}/guilds/${this.options.guildId}/${kind}`);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();
            this.cache[kind] = data[kind === 'emojis' ? 'emojis' : kind] ?? data;
            return this.cache[kind];
        } catch (err) {
            console.error('EmbedGenerator: error cargando', kind, err);
            return null;
        }
    }

    // ------------------------------------------------------------------
    // API pública
    // ------------------------------------------------------------------

    getState() {
        return JSON.parse(JSON.stringify(this.state));
    }

    setState(newState) {
        this.state = { ...this.state, ...newState };
        const q = (sel) => this.container.querySelector(sel);
        q('#eg-author').value = this.state.author || '';
        q('#eg-author-icon').value = this.state.authorIcon || '';
        q('#eg-title').value = this.state.title || '';
        q('#eg-description').value = this.state.description || '';
        q('#eg-color').value = this.state.color || '#ff4d8b';
        q('#eg-footer').value = this.state.footer || '';
        q('#eg-footer-icon').value = this.state.footerIcon || '';
        this.renderFields();
        this.updatePreview();
    }
}
