import { PermissionFlagsBits, ChannelType } from 'discord.js';
import { slash } from '../../lib/slash.js';
import { esPt } from '../../lib/localize.js';
import { getGuildConfig, updateGuildConfig } from '../../services/guildConfig.js';
import MessagesService from '../../services/messagesService.js';

export const data = slash('channel', 'Create channels and configure join-to-create', {
    names: esPt('canal'),
    descriptions: esPt('Crea canales y configura el join-to-create', 'Cria canais e configura o join-to-create'),
    permissions: PermissionFlagsBits.ManageChannels
})
    .addSubcommand(sub => sub.setName('create').setDescription('Create a text or voice channel')
        .addStringOption(o => o.setName('name').setDescription('Channel name').setRequired(true))
        .addStringOption(o => o.setName('type').setDescription('Voice or text').setRequired(true)
            .addChoices({ name: 'Voice', value: 'voice' }, { name: 'Text', value: 'text' }))
        .addChannelOption(o => o.setName('category').setDescription('Category (optional)')
            .addChannelTypes(ChannelType.GuildCategory)))
    .addSubcommand(sub => sub.setName('hub').setDescription('Set the join-to-create hub voice channel')
        .addChannelOption(o => o.setName('channel').setDescription('Voice channel hub')
            .addChannelTypes(ChannelType.GuildVoice).setRequired(true)))
    .addSubcommand(sub => sub.setName('hub-remove').setDescription('Disable join-to-create'))
    .addSubcommand(sub => sub.setName('template').setDescription('Temporary channel name template ({usuario})')
        .addStringOption(o => o.setName('template').setDescription('Name template. Empty = reset.')));

async function requireManageGuild(interaction, guildId) {
    if (interaction.memberPermissions.has(PermissionFlagsBits.ManageGuild)) return true;
    await interaction.reply({ content: MessagesService.get(guildId, 'Errores:SinPermiso'), ephemeral: true });
    return false;
}

export async function execute(interaction) {
    const sub = interaction.options.getSubcommand();
    const guildId = interaction.guildId;

    if (sub === 'create') {
        const name = interaction.options.getString('name');
        const type = interaction.options.getString('type');
        const category = interaction.options.getChannel('category');
        const created = await interaction.guild.channels.create({
            name,
            type: type === 'voice' ? ChannelType.GuildVoice : ChannelType.GuildText,
            parent: category ? category.id : undefined,
            reason: 'Created with /channel create'
        });
        return interaction.reply({ content: MessagesService.get(guildId, 'Voces:Creado', { canal: created.toString() }) });
    }

    if (!await requireManageGuild(interaction, guildId)) return;

    if (sub === 'hub') {
        const channel = interaction.options.getChannel('channel');
        if (channel.type !== ChannelType.GuildVoice) {
            return interaction.reply({ content: MessagesService.get(guildId, 'Voces:HubDebeSerVoz'), ephemeral: true });
        }
        updateGuildConfig(guildId, { HubChannelId: channel.id });
        return interaction.reply({ content: MessagesService.get(guildId, 'Voces:HubEstablecido', { canal: channel.toString() }) });
    }

    if (sub === 'hub-remove') {
        const cfg = getGuildConfig(guildId);
        if (!cfg?.HubChannelId) {
            return interaction.reply({ content: MessagesService.get(guildId, 'Voces:HubQuitado'), ephemeral: true });
        }
        updateGuildConfig(guildId, { HubChannelId: null });
        return interaction.reply({ content: MessagesService.get(guildId, 'Voces:HubQuitado') });
    }

    if (sub === 'template') {
        const template = interaction.options.getString('template');
        if (!template?.trim()) {
            updateGuildConfig(guildId, { TempChannelNameTemplate: null });
            return interaction.reply({ content: MessagesService.get(guildId, 'Voces:PlantillaBorrada') });
        }
        if (template.length > 100) {
            return interaction.reply({ content: MessagesService.get(guildId, 'Voces:PlantillaLarga'), ephemeral: true });
        }
        updateGuildConfig(guildId, { TempChannelNameTemplate: template });
        return interaction.reply({
            content: MessagesService.get(guildId, 'Voces:PlantillaEstablecida', {
                vista: template.replace('{usuario}', interaction.user.username)
            })
        });
    }
}
