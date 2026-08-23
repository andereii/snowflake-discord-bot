export function setupSidebar() {
    const items = document.querySelectorAll('.sidebar-menu-item');
    const sections = document.querySelectorAll('.settings-section');

    items.forEach(item => {
        item.addEventListener('click', (e) => {
            e.preventDefault();
            
            // Obtener el ID de la sección destino quitando el '#'
            const targetId = item.getAttribute('href').substring(1);
            const targetSection = document.getElementById(targetId);

            // Solo hacemos el cambio visual si la sección existe en el HTML
            if (targetSection) {
                // Cambiar tab activa en el sidebar
                items.forEach(i => i.classList.remove('active'));
                item.classList.add('active');

                // Ocultar todas las secciones y mostrar la seleccionada
                sections.forEach(sec => sec.classList.remove('active'));
                targetSection.classList.add('active');
            } else {
                // Si la sección no existe en tu HTML, avisamos al usuario
                showToast('Esta sección aún está en construcción.', 'error');
            }
        });
    });
}

export function showToast(message, type = 'success') {
    const container = document.getElementById('toast-container');
    
    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    
    const icon = type === 'success' ? 'fa-circle-check' : 'fa-circle-exclamation';
    toast.innerHTML = `<i class="fa-solid ${icon}"></i> <span>${message}</span>`;
    
    container.appendChild(toast);
    
    // Animar entrada
    setTimeout(() => toast.classList.add('show'), 10);
    
    // Animar salida y eliminar
    setTimeout(() => {
        toast.classList.remove('show');
        setTimeout(() => toast.remove(), 300);
    }, 3000);
}

export function showToast(message, type = 'success') {
    const container = document.getElementById('toast-container');
    
    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    
    const icon = type === 'success' ? 'fa-circle-check' : 'fa-circle-exclamation';
    toast.innerHTML = `<i class="fa-solid ${icon}"></i> <span>${message}</span>`;
    
    container.appendChild(toast);
    
    // Animar entrada
    setTimeout(() => toast.classList.add('show'), 10);
    
    // Animar salida y eliminar
    setTimeout(() => {
        toast.classList.remove('show');
        setTimeout(() => toast.remove(), 300);
    }, 3000);
}
