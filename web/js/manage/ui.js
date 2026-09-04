export function setupSidebar() {
    const items = document.querySelectorAll('.sidebar-menu-item');
    const sections = document.querySelectorAll('.settings-section');

    items.forEach(item => {
        item.addEventListener('click', (e) => {
            e.preventDefault();

            const targetId = item.getAttribute('href').substring(1);
            const targetSection = document.getElementById(targetId);

            if (targetSection) {
                items.forEach(i => i.classList.remove('active'));
                item.classList.add('active');

                sections.forEach(sec => sec.classList.remove('active'));
                targetSection.classList.add('active');
            } else {
                showToast('Esta sección aún está en construcción.', 'error');
            }
        });
    });
}

export function showToast(message, type = 'success') {
    const container = document.getElementById('toast-container');
    if (!container) return;

    const toast = document.createElement('div');
    toast.className = `toast ${type}`;

    const icon = type === 'success' ? 'fa-circle-check' : 'fa-circle-exclamation';
    toast.innerHTML = `<i class="fa-solid ${icon}"></i> <span>${message}</span>`;

    container.appendChild(toast);

    setTimeout(() => toast.classList.add('show'), 10);

    setTimeout(() => {
        toast.classList.remove('show');
        setTimeout(() => toast.remove(), 300);
    }, 3000);
}

/** Fija el texto de un botón durante una operación asíncrona. Devuelve un handler "finally". */
export function withLoading(btn, loadingText) {
    const original = btn.innerHTML;
    btn.innerHTML = `<i class="fa-solid fa-spinner fa-spin"></i> ${loadingText}`;
    btn.style.pointerEvents = 'none';
    return () => {
        btn.innerHTML = original;
        btn.style.pointerEvents = 'auto';
    };
}

/** Lee un input, recortando espacios; devuelve null si está vacío. */
export function readOptional(id) {
    const value = document.getElementById(id)?.value?.trim();
    return value ? value : null;
}

/** Lee un número de un input; devuelve null si no hay valor válido. */
export function readOptionalNumber(id) {
    const raw = document.getElementById(id)?.value?.trim();
    if (raw === '' || raw === undefined) return null;
    const n = Number(raw);
    return Number.isFinite(n) ? n : null;
}
