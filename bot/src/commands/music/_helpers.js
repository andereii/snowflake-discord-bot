import { getQueue, canControlMusic } from '../../services/music.js';
import { refreshWidgetIfExists, sendOrUpdateWidget, stopWidget } from '../../services/musicWidget.js';
import MessagesService from '../../services/messagesService.js';

export async function requireQueue(interaction, { playing = true } = {}) {
    const guildId = interaction.guildId;
    const queue = getQueue(guildId);
    if (!queue || (playing && !queue.playing)) {
        await interaction.reply({ content: MessagesService.get(guildId, 'Musica:NoActivo'), ephemeral: true });
        return null;
    }
    return { guildId, queue };
}

export async function requireControl(interaction) {
    const access = canControlMusic(interaction);
    if (access.ok) return true;
    await interaction.reply({ content: access.message, ephemeral: true });
    return false;
}

export function refreshWidget(interaction) {
    return refreshWidgetIfExists(interaction.guildId, interaction.channel);
}

export function showWidget(interaction) {
    return sendOrUpdateWidget(interaction.channel, interaction.guildId);
}

export function endWidget(interaction) {
    return stopWidget(interaction.guildId, interaction.channel);
}
