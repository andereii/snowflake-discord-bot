import { SlashCommandBuilder } from 'discord.js';
import { localize } from './localize.js';

export function slash(name, description, { names, descriptions, permissions } = {}) {
    const data = new SlashCommandBuilder().setName(name).setDescription(description);
    localize(data, { name: names, description: descriptions });
    if (permissions != null) data.setDefaultMemberPermissions(permissions);
    return data;
}

export function stateOption(builder, description = 'Enable or disable (empty = show current)') {
    return builder.addStringOption(option =>
        option.setName('state')
            .setDescription(description)
            .addChoices(
                { name: 'Enable', value: 'on' },
                { name: 'Disable', value: 'off' }
            )
    );
}

export function userOption(builder, { description = 'Target user', required = true } = {}) {
    return builder.addUserOption(option =>
        option.setName('user').setDescription(description).setRequired(required)
    );
}

export function reasonOption(builder, { required = false } = {}) {
    return builder.addStringOption(option =>
        option.setName('reason').setDescription('Reason').setRequired(required)
    );
}
