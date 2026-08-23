import { CLIENT_ID, REDIRECT_URI } from './config.js';

export function setupModals() {
    const overlays = document.querySelectorAll('.modal-overlay');
    const closeBtns = document.querySelectorAll('.close-modal');
    
    closeBtns.forEach(btn => {
        btn.addEventListener('click', () => {
            overlays.forEach(m => m.classList.remove('show'));
        });
    });
    
    overlays.forEach(overlay => {
        overlay.addEventListener('click', (e) => {
            if (e.target === overlay) {
                overlay.classList.remove('show');
            }
        });
    });
}

export function renderUserPill(user) {
    const authContainer = document.getElementById('auth-container');
    const template = document.getElementById('user-pill-template');

    if (authContainer && template) {
        authContainer.replaceChildren();
        const clone = template.content.cloneNode(true);
        clone.getElementById('user-tag-text').textContent = `@${user.username}`;
        const avatarImg = clone.getElementById('user-avatar-img');
        if (user.avatar) {
            avatarImg.src = `https://cdn.discordapp.com/avatars/${user.id}/${user.avatar}.png`;
        } else {
            avatarImg.src = `https://cdn.discordapp.com/embed/avatars/${parseInt(user.discriminator || '0') % 5}.png`;
        }
        authContainer.appendChild(clone);
        setupDropdownEvents();
    }
}

export function setupDropdownEvents() {
    const pillBtn = document.getElementById('user-pill-btn');
    const dropdownMenu = document.getElementById('user-dropdown-menu');
    const logoutBtn = document.getElementById('logout-btn');

    pillBtn.addEventListener('click', (event) => {
        event.stopPropagation();
        dropdownMenu.classList.toggle('show');
        pillBtn.setAttribute('aria-expanded', dropdownMenu.classList.contains('show').toString());
    });
    logoutBtn.addEventListener('click', () => {
        localStorage.removeItem('discord_token');
        window.location.reload(); 
    });
    document.addEventListener('click', (event) => {
        if (!pillBtn.contains(event.target) && !dropdownMenu.contains(event.target)) {
            dropdownMenu.classList.remove('show');
            pillBtn.setAttribute('aria-expanded', 'false');
        }
    });
}

export function renderServerCards(validGuilds) {
    const grid = document.getElementById('servers-grid');
    const template = document.getElementById('server-card-template');
    
    const inviteList = document.getElementById('invite-servers-list');
    const inviteTemplate = document.getElementById('invite-item-template');
    
    document.getElementById('login-prompt').classList.remove('show');
    document.getElementById('servers-dashboard').classList.add('show');

    if (!grid || !template) return;

    grid.replaceChildren();
    if(inviteList) inviteList.replaceChildren();

    const botGuilds = validGuilds.filter(g => g.hasBot);
    const inviteGuilds = validGuilds.filter(g => !g.hasBot);

    botGuilds.forEach(guild => {
        const clone = template.content.cloneNode(true);
        
        clone.querySelector('.server-name').textContent = guild.name;
        
        const iconImg = clone.querySelector('.server-icon');
        iconImg.src = guild.icon ? `https://cdn.discordapp.com/icons/${guild.id}/${guild.icon}.png` : 'https://cdn.discordapp.com/embed/avatars/0.png';
        
        const banner = clone.querySelector('.server-banner');
        if (guild.banner) {
            banner.style.backgroundImage = `url(https://cdn.discordapp.com/banners/${guild.id}/${guild.banner}.png?size=480)`;
        }

        const manageBtn = clone.querySelector('.btn-manage');
        manageBtn.addEventListener('click', () => {
            abrirConfigModal(guild);
        });

        grid.appendChild(clone);
    });

    const addCard = document.createElement('div');
    addCard.className = 'server-card add-server-card';
    addCard.innerHTML = `<i class="fa-solid fa-plus"></i><h3>Agregar servidor</h3>`;
    addCard.addEventListener('click', () => {
        document.getElementById('add-server-modal').classList.add('show');
    });
    grid.appendChild(addCard);

    if (inviteTemplate && inviteList) {
        if (inviteGuilds.length === 0) {
            inviteList.innerHTML = '<p style="text-align:center; color: var(--color-text-muted);">No tienes servidores disponibles para agregar el bot.</p>';
        } else {
            inviteGuilds.forEach(guild => {
                const clone = inviteTemplate.content.cloneNode(true);
                clone.querySelector('.invite-name').textContent = guild.name;
                
                const icon = clone.querySelector('.invite-icon');
                icon.src = guild.icon ? `https://cdn.discordapp.com/icons/${guild.id}/${guild.icon}.png` : 'https://cdn.discordapp.com/embed/avatars/0.png';
                
                const btn = clone.querySelector('.btn-invite');
                btn.href = `https://discord.com/api/oauth2/authorize?client_id=${CLIENT_ID}&permissions=8&scope=bot%20applications.commands&guild_id=${guild.id}&redirect_uri=${REDIRECT_URI}&response_type=token`;
                
                inviteList.appendChild(clone);
            });
        }
    }
}

export function abrirConfigModal(guild) {
    const modal = document.getElementById('config-modal');
    const iframe = document.getElementById('config-iframe');
    const title = document.getElementById('config-server-name');
    const icon = document.getElementById('config-server-icon'); // Nuevo icono cuadrado
    
    title.textContent = guild.name;
    icon.src = guild.icon ? `https://cdn.discordapp.com/icons/${guild.id}/${guild.icon}.png` : 'https://cdn.discordapp.com/embed/avatars/0.png';
    iframe.src = `manage.html?server_id=${guild.id}`;
    
    modal.classList.add('show');
}
