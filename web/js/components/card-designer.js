// Card Designer Component
export class CardDesigner {
    constructor(container, options = {}) {
        this.container = typeof container === 'string' ? document.querySelector(container) : container;
        this.options = {
            defaultFont: 'Inter',
            defaultTextColor: '#FFFFFF',
            defaultOverlayColor: '#000000',
            defaultOverlayIntensity: 0.75,
            defaultTitle: '¡{{username}} ingresó al servidor!',
            defaultSubtitle: 'Ahora somos {{members}} miembros',
            ...options
        };

        this.state = {
            font: this.options.defaultFont,
            textColor: this.options.defaultTextColor,
            overlayColor: this.options.defaultOverlayColor,
            overlayIntensity: this.options.defaultOverlayIntensity,
            backgroundImage: null,
            backgroundBlur: false,
            title: this.options.defaultTitle,
            subtitle: this.options.defaultSubtitle,
            avatarUrl: 'https://cdn.discordapp.com/embed/avatars/0.png'
        };

        this.availableFonts = [
            'Inter', 'Roboto', 'Open Sans', 'Lato', 'Montserrat',
            'Poppins', 'Raleway', 'Nunito', 'Ubuntu', 'Playfair Display'
        ];

        this.availableColors = [
            '#FFFFFF', '#E0E0E0', '#FF6B6B', '#FFD93D', '#6BCB77',
            '#4D96FF', '#C77DFF', '#FF9A3D', '#00D9FF', '#FF4D8B'
        ];

        this.variables = [
            { name: '{{username}}', description: 'Nombre del usuario' },
            { name: '{{members}}', description: 'Número de miembros' },
            { name: '{{server}}', description: 'Nombre del servidor' },
            { name: '{{date}}', description: 'Fecha actual' }
        ];

        this.render();
        this.attachEventListeners();
        this.updatePreview();
    }

    render() {
        this.container.innerHTML = `
            <div class="card-designer">
                <div class="card-designer-preview">
                    <div class="card-designer-preview-image"></div>
                    <div class="card-designer-preview-overlay"></div>
                    <div class="card-designer-preview-content">
                        <img class="card-designer-preview-avatar" src="${this.state.avatarUrl}" alt="Avatar">
                        <div class="card-designer-preview-title"></div>
                        <div class="card-designer-preview-subtitle"></div>
                    </div>
                </div>

                <div class="card-designer-section">
                    <div class="card-designer-section-title">Estilo de Fuente</div>
                    <div class="card-designer-section-description">Elige una fuente que combine con la vibra de tu servidor.</div>
                    <select class="card-designer-select" id="font-select">
                        ${this.availableFonts.map(font => `
                            <option value="${font}" ${font === this.state.font ? 'selected' : ''}>${font}</option>
                        `).join('')}
                    </select>
                </div>

                <div class="card-designer-section">
                    <div class="card-designer-section-title">Color del Texto</div>
                    <div class="card-designer-section-description">Elige un color que destaque sobre tu fondo.</div>
                    <div class="card-designer-color-palette" id="text-color-palette">
                        ${this.availableColors.map(color => `
                            <div class="card-designer-color-swatch ${color === this.state.textColor ? 'active' : ''}" 
                                 style="background: ${color}" 
                                 data-color="${color}"></div>
                        `).join('')}
                        <button class="card-designer-color-picker-btn" id="text-color-picker-btn">🎨</button>
                    </div>
                </div>

                <div class="card-designer-section">
                    <div class="card-designer-section-title">Color de Superposición</div>
                    <div class="card-designer-section-description">Añade una capa de color sobre el fondo para mejorar la legibilidad.</div>
                    <div class="card-designer-color-palette" id="overlay-color-palette">
                        ${this.availableColors.map(color => `
                            <div class="card-designer-color-swatch ${color === this.state.overlayColor ? 'active' : ''}" 
                                 style="background: ${color}" 
                                 data-color="${color}"></div>
                        `).join('')}
                        <button class="card-designer-color-picker-btn" id="overlay-color-picker-btn">🎨</button>
                    </div>
                </div>

                <div class="card-designer-section">
                    <div class="card-designer-section-title">Intensidad de Superposición</div>
                    <div class="card-designer-section-description">Controla cuánto oscurece la superposición tu fondo.</div>
                    <div class="card-designer-slider-label">
                        <span>Intensidad</span>
                        <span id="overlay-intensity-value">${Math.round(this.state.overlayIntensity * 100)}%</span>
                    </div>
                    <input type="range" class="card-designer-slider" id="overlay-intensity" 
                           min="0" max="100" value="${Math.round(this.state.overlayIntensity * 100)}">
                    <div class="card-designer-checkbox-group">
                        <input type="checkbox" class="card-designer-checkbox" id="background-blur">
                        <label for="background-blur" class="card-designer-checkbox-label">Desenfocar fondo</label>
                    </div>
                </div>

                <div class="card-designer-section">
                    <div class="card-designer-section-title">Imagen de Fondo</div>
                    <div class="card-designer-section-description">Sube una imagen para usar como fondo de tu tarjeta.</div>
                    <label class="card-designer-upload">
                        <span class="card-designer-upload-icon">🖼️</span>
                        <span class="card-designer-upload-text">Subir Imagen</span>
                        <input type="file" id="background-upload" accept="image/*" style="display: none;">
                    </label>
                </div>

                <div class="card-designer-section">
                    <div class="card-designer-section-title">Título de la Tarjeta</div>
                    <div class="card-designer-section-description">El texto principal que se muestra en tu tarjeta.</div>
                    <div class="card-designer-input-group">
                        <input type="text" class="card-designer-input" id="title-input" 
                               value="${this.state.title}" placeholder="Título de la tarjeta">
                        <button class="card-designer-variables-btn" id="title-variables-btn">{}</button>
                    </div>
                </div>

                <div class="card-designer-section">
                    <div class="card-designer-section-title">Subtítulo de la Tarjeta</div>
                    <div class="card-designer-section-description">Texto secundario que aparece debajo del título.</div>
                    <div class="card-designer-input-group">
                        <input type="text" class="card-designer-input" id="subtitle-input" 
                               value="${this.state.subtitle}" placeholder="Subtítulo de la tarjeta">
                        <button class="card-designer-variables-btn" id="subtitle-variables-btn">{}</button>
                    </div>
                </div>
            </div>
        `;

        // Cargar fuente de Google Fonts
        this.loadGoogleFont(this.state.font);
    }

    loadGoogleFont(fontName) {
        const link = document.createElement('link');
        link.href = `https://fonts.googleapis.com/css2?family=${fontName.replace(' ', '+')}:wght@400;600;700&display=swap`;
        link.rel = 'stylesheet';
        document.head.appendChild(link);
    }

    attachEventListeners() {
        // Font select
        const fontSelect = this.container.querySelector('#font-select');
        fontSelect.addEventListener('change', (e) => {
            this.state.font = e.target.value;
            this.loadGoogleFont(e.target.value);
            this.updatePreview();
        });

        // Text color palette
        const textColorSwatches = this.container.querySelectorAll('#text-color-palette .card-designer-color-swatch');
        textColorSwatches.forEach(swatch => {
            swatch.addEventListener('click', () => {
                textColorSwatches.forEach(s => s.classList.remove('active'));
                swatch.classList.add('active');
                this.state.textColor = swatch.dataset.color;
                this.updatePreview();
            });
        });

        // Text color picker button
        const textColorPickerBtn = this.container.querySelector('#text-color-picker-btn');
        textColorPickerBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            this.showColorPicker('text', e.target);
        });

        // Overlay color palette
        const overlayColorSwatches = this.container.querySelectorAll('#overlay-color-palette .card-designer-color-swatch');
        overlayColorSwatches.forEach(swatch => {
            swatch.addEventListener('click', () => {
                overlayColorSwatches.forEach(s => s.classList.remove('active'));
                swatch.classList.add('active');
                this.state.overlayColor = swatch.dataset.color;
                this.updatePreview();
            });
        });

        // Overlay color picker button
        const overlayColorPickerBtn = this.container.querySelector('#overlay-color-picker-btn');
        overlayColorPickerBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            this.showColorPicker('overlay', e.target);
        });

        // Overlay intensity slider
        const overlayIntensity = this.container.querySelector('#overlay-intensity');
        const overlayIntensityValue = this.container.querySelector('#overlay-intensity-value');
        overlayIntensity.addEventListener('input', (e) => {
            this.state.overlayIntensity = e.target.value / 100;
            overlayIntensityValue.textContent = `${e.target.value}%`;
            this.updatePreview();
        });

        // Background blur checkbox
        const backgroundBlur = this.container.querySelector('#background-blur');
        backgroundBlur.addEventListener('change', (e) => {
            this.state.backgroundBlur = e.target.checked;
            this.updatePreview();
        });

        // Background upload
        const backgroundUpload = this.container.querySelector('#background-upload');
        backgroundUpload.addEventListener('change', (e) => {
            const file = e.target.files[0];
            if (file) {
                const reader = new FileReader();
                reader.onload = (event) => {
                    this.state.backgroundImage = event.target.result;
                    this.updatePreview();
                };
                reader.readAsDataURL(file);
            }
        });

        // Title input
        const titleInput = this.container.querySelector('#title-input');
        titleInput.addEventListener('input', (e) => {
            this.state.title = e.target.value;
            this.updatePreview();
        });

        // Subtitle input
        const subtitleInput = this.container.querySelector('#subtitle-input');
        subtitleInput.addEventListener('input', (e) => {
            this.state.subtitle = e.target.value;
            this.updatePreview();
        });

        // Title variables button
        const titleVariablesBtn = this.container.querySelector('#title-variables-btn');
        titleVariablesBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            this.showVariablesPopup(titleInput, e.target);
        });

        // Subtitle variables button
        const subtitleVariablesBtn = this.container.querySelector('#subtitle-variables-btn');
        subtitleVariablesBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            this.showVariablesPopup(subtitleInput, e.target);
        });

        // Close popups on outside click
        document.addEventListener('click', () => {
            this.closeAllPopups();
        });
    }

    updatePreview() {
        const previewImage = this.container.querySelector('.card-designer-preview-image');
        const previewOverlay = this.container.querySelector('.card-designer-preview-overlay');
        const previewTitle = this.container.querySelector('.card-designer-preview-title');
        const previewSubtitle = this.container.querySelector('.card-designer-preview-subtitle');

        // Update background image
        if (this.state.backgroundImage) {
            previewImage.style.backgroundImage = `url(${this.state.backgroundImage})`;
            previewImage.style.filter = this.state.backgroundBlur ? 'blur(10px)' : 'none';
        } else {
            previewImage.style.backgroundImage = 'none';
        }

        // Update overlay
        previewOverlay.style.backgroundColor = this.state.overlayColor;
        previewOverlay.style.opacity = this.state.overlayIntensity;

        // Update text
        previewTitle.textContent = this.state.title;
        previewTitle.style.color = this.state.textColor;
        previewTitle.style.fontFamily = `'${this.state.font}', sans-serif`;

        previewSubtitle.textContent = this.state.subtitle;
        previewSubtitle.style.color = this.state.textColor;
        previewSubtitle.style.opacity = 0.8;
        previewSubtitle.style.fontFamily = `'${this.state.font}', sans-serif`;
    }

    showColorPicker(type, targetElement) {
        this.closeAllPopups();

        const popup = document.createElement('div');
        popup.className = 'card-designer-color-picker';
        popup.style.position = 'absolute';
        
        const rect = targetElement.getBoundingClientRect();
        popup.style.top = `${rect.bottom + window.scrollY + 8}px`;
        popup.style.left = `${rect.left + window.scrollX}px`;

        popup.innerHTML = `
            <div class="card-designer-color-wheel" id="color-wheel-${type}"></div>
            <div class="card-designer-color-hex">
                <input type="text" class="card-designer-color-hex-input" id="color-hex-${type}" 
                       value="${type === 'text' ? this.state.textColor : this.state.overlayColor}" 
                       placeholder="#FFFFFF">
            </div>
        `;

        document.body.appendChild(popup);

        // Hex input handler
        const hexInput = popup.querySelector(`#color-hex-${type}`);
        hexInput.addEventListener('input', (e) => {
            const color = e.target.value;
            if (/^#[0-9A-F]{6}$/i.test(color)) {
                if (type === 'text') {
                    this.state.textColor = color;
                } else {
                    this.state.overlayColor = color;
                }
                this.updatePreview();
            }
        });

        // Color wheel handler (simplified)
        const colorWheel = popup.querySelector(`#color-wheel-${type}`);
        colorWheel.addEventListener('click', (e) => {
            const rect = colorWheel.getBoundingClientRect();
            const x = e.clientX - rect.left;
            const y = e.clientY - rect.top;
            const centerX = rect.width / 2;
            const centerY = rect.height / 2;
            const angle = Math.atan2(y - centerY, x - centerX);
            const distance = Math.sqrt(Math.pow(x - centerX, 2) + Math.pow(y - centerY, 2));
            const maxDistance = rect.width / 2;
            const saturation = Math.min(1, distance / maxDistance);
            const hue = (angle + Math.PI) / (2 * Math.PI);
            const lightness = 0.5;
            const color = this.hslToHex(hue * 360, saturation * 100, lightness * 100);
            hexInput.value = color;
            hexInput.dispatchEvent(new Event('input'));
        });
    }

    showVariablesPopup(inputElement, targetElement) {
        this.closeAllPopups();

        const popup = document.createElement('div');
        popup.className = 'card-designer-variables-popup';
        popup.style.position = 'absolute';
        
        const rect = targetElement.getBoundingClientRect();
        popup.style.top = `${rect.bottom + window.scrollY + 8}px`;
        popup.style.left = `${rect.left + window.scrollX}px`;

        popup.innerHTML = `
            <ul class="card-designer-variables-list">
                ${this.variables.map(v => `
                    <li data-variable="${v.name}" title="${v.description}">${v.name}</li>
                `).join('')}
            </ul>
        `;

        document.body.appendChild(popup);

        // Variable click handler
        const variableItems = popup.querySelectorAll('.card-designer-variables-list li');
        variableItems.forEach(item => {
            item.addEventListener('click', () => {
                const variable = item.dataset.variable;
                const cursorPos = inputElement.selectionStart;
                const textBefore = inputElement.value.substring(0, cursorPos);
                const textAfter = inputElement.value.substring(cursorPos);
                inputElement.value = textBefore + variable + textAfter;
                inputElement.focus();
                inputElement.selectionStart = inputElement.selectionEnd = cursorPos + variable.length;
                inputElement.dispatchEvent(new Event('input'));
                this.closeAllPopups();
            });
        });
    }

    closeAllPopups() {
        document.querySelectorAll('.card-designer-color-picker, .card-designer-variables-popup').forEach(popup => {
            popup.remove();
        });
    }

    hslToHex(h, s, l) {
        l /= 100;
        const a = s * Math.min(l, 1 - l) / 100;
        const f = n => {
            const k = (n + h / 30) % 12;
            const color = l - a * Math.max(Math.min(k - 3, 9 - k, 1), -1);
            return Math.round(255 * color).toString(16).padStart(2, '0');
        };
        return `#${f(0)}${f(8)}${f(4)}`;
    }

    getState() {
        return { ...this.state };
    }

    setState(newState) {
        this.state = { ...this.state, ...newState };
        this.updatePreview();
    }
}
