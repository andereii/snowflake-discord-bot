import { SlashCommandBuilder, PermissionFlagsBits, ChannelType } from 'discord.js';
import db from '../services/database.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
    .setName('channel')
    .setDescription('Create channels and configure join-to-create')
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageChannels)
    .addSubcommand(sub =>
        sub.setName('create')
            .setDescription('Create a text or voice channel on demand')
            .addStringOption(opt =>
                opt.setName('name')
                    .setDescription('Channel name')
                    .setRequired(true))
            .addStringOption(opt =>
                opt.setName('type')
                    .setDescription('Voice or text')
                    .setRequired(true)
                    .addChoices(
                        { name: 'Voice', value: 'voice' },
                        { name: 'Text', value: 'text' }
                    ))
            .addChannelOption(opt =>
                opt.setName('category')
                    .setDescription('Category to create it in (optional)')
                    .addChannelTypes(ChannelType.GuildCategory)
                    .setRequired(false)))
    .addSubcommand(sub =>
        sub.setName('hub')
            .setDescription('Set the join-to-create hub voice channel')
            .addChannelOption(opt =>
                opt.setName('channel')
                    .setDescription('Voice channel to act as the hub')
                    .addChannelTypes(ChannelType.GuildVoice)
                    .setRequired(true)))
    .addSubcommand(sub =>
        sub.setName('hub-remove')
            .setDescription('Disable join-to-create'))
    .addSubcommand(sub =>
        sub.setName('template')
            .setDescription('Customize temporary channel names ({usuario} placeholder; empty = default)')
            .addStringOption(opt =>
                opt.setName('template')
                    .setDescription('Name template, e.g. "🔊 {usuario}". Empty = reset.')
                    .setRequired(false)));

export async function execute(interaction) {
    const subcommand = interaction.options.getSubcommand();
    const guildId = interaction.guildId;

    if (subcommand === 'create') {
        const nombre = interaction.options.getString('name');
        const tipo = interaction.options.getString('type');
        const categoria = interaction.options.getChannel('category');

        const channelType = tipo === 'voice' ? ChannelType.GuildVoice : ChannelType.GuildText;

        const newChannel = await interaction.guild.channels.create({
            name: nombre,
            type: channelType,
            parent: categoria ? categoria.id : undefined,
            reason: 'Creado con /channel create'
        });

        await interaction.reply({
            content: MessagesService.get(guildId, 'Voces:Creado', { canal: newChannel.toString() })
        });
    } else if (subcommand === 'hub') {
        if (!interaction.memberPermissions.has(PermissionFlagsBits.ManageGuild)) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'Errores:SinPermiso'),
                ephemeral: true
            });
        }

        const canal = interaction.options.getChannel('channel');
        if (canal.type !== ChannelType.GuildVoice) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'Voces:HubDebeSerVoz'),
                ephemeral: true
            });
        }

        db.prepare('INSERT OR IGNORE INTO GuildConfigs (GuildId) VALUES (?)').run(guildId);
        db.prepare('UPDATE GuildConfigs SET HubChannelId = ? WHERE GuildId = ?').run(canal.id, guildId);

        await interaction.reply({
            content: MessagesService.get(guildId, 'Voces:HubEstablecido', { canal: canal.toString() })
        });
    } else if (subcommand === 'hub-remove') {
        if (!interaction.memberPermissions.has(PermissionFlagsBits.ManageGuild)) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'Errores:SinPermiso'),
                ephemeral: true
            });
        }

        const config = db.prepare('SELECT HubChannelId FROM GuildConfigs WHERE GuildId = ?').get(guildId);
        if (!config || !config.HubChannelId) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'Voces:HubQuitado'),
                ephemeral: true
            });
        }

        db.prepare('UPDATE GuildConfigs SET HubChannelId = NULL WHERE GuildId = ?').run(guildId);
        await interaction.reply({
            content: MessagesService.get(guildId, 'Voces:HubQuitado')
        });
    } else if (subcommand === 'template') {
        if (!interaction.memberPermissions.has(PermissionFlagsBits.ManageGuild)) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'Errores:SinPermiso'),
                ephemeral: true
            });
        }

        const plantilla = interaction.options.getString('template');

        if (!plantilla || !plantilla.trim()) {
            db.prepare('UPDATE GuildConfigs SET TempChannelNameTemplate = NULL WHERE GuildId = ?').run(guildId);
            return interaction.reply({
                content: MessagesService.get(guildId, 'Voces:PlantillaBorrada')
            });
        }

        if (plantilla.length > 100) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'Voces:PlantillaLarga'),
                ephemeral: true
            });
        }

        db.prepare('INSERT OR IGNORE INTO GuildConfigs (GuildId) VALUES (?)').run(guildId);
        db.prepare('UPDATE GuildConfigs SET TempChannelNameTemplate = ? WHERE GuildId = ?').run(plantilla, guildId);

        await interaction.reply({
            content: MessagesService.get(guildId, 'Voces:PlantillaEstablecida', {
                vista: plantilla.replace('{usuario}', interaction.user.username)
            })
        });
    }
}
