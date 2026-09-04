import { SlashCommandBuilder, EmbedBuilder, PermissionFlagsBits } from 'discord.js';
import db from '../services/database.js';
import MessagesService from '../services/messagesService.js';

export const data = new SlashCommandBuilder()
    .setName('welcome')
    .setDescription('Configure welcome messages')
    .setDefaultMemberPermissions(PermissionFlagsBits.ManageGuild)
    .addSubcommand(subcommand =>
        subcommand
            .setName('channel')
            .setDescription('Set the welcome channel for new members')
            .addChannelOption(option =>
                option.setName('channel')
                    .setDescription('Text channel for welcomes')
                    .setRequired(true)
            )
    )
    .addSubcommand(subcommand =>
        subcommand
            .setName('message')
            .setDescription('Set custom welcome message (use {user} and {server})')
            .addStringOption(option =>
                option.setName('text')
                    .setDescription('Welcome message. Placeholders: {user} {server}. Max 1900 chars.')
                    .setRequired(true)
            )
    )
    .addSubcommand(subcommand =>
        subcommand
            .setName('view')
            .setDescription('Show current welcome configuration')
    )
    .addSubcommand(subcommand =>
        subcommand
            .setName('disable')
            .setDescription('Disable welcome messages')
    );

export async function execute(interaction) {
    const guildId = interaction.guild.id;
    const subCommand = interaction.options.getSubcommand();

    db.prepare(`INSERT OR IGNORE INTO GuildConfigs (GuildId) VALUES (?)`).run(guildId);

    if (subCommand === 'channel') {
        const canal = interaction.options.getChannel('channel');
        db.prepare(`UPDATE GuildConfigs SET WelcomeChannelId = ? WHERE GuildId = ?`).run(canal.id, guildId);
        await interaction.reply({
            content: MessagesService.get(guildId, 'Bienvenida:ConfigCanalExito', { canal: canal.toString() }),
            ephemeral: true
        });
    } 
    else if (subCommand === 'message') {
        const mensaje = interaction.options.getString('text');
        if (mensaje.length > 1900) {
            await interaction.reply({
                content: MessagesService.get(guildId, 'Bienvenida:MensajeLargo'),
                ephemeral: true
            });
            return;
        }
        db.prepare(`UPDATE GuildConfigs SET WelcomeMessage = ? WHERE GuildId = ?`).run(mensaje, guildId);
        const vista = mensaje
            .replace(/{usuario}|{user}/g, interaction.user.toString())
            .replace(/{servidor}|{server}/g, interaction.guild.name);
        
        await interaction.reply({
            content: MessagesService.get(guildId, 'Bienvenida:ConfigMensajeExito', { vista }),
            ephemeral: true
        });
    }
    else if (subCommand === 'view') {
        const row = db.prepare(`SELECT WelcomeChannelId, WelcomeMessage FROM GuildConfigs WHERE GuildId = ?`).get(guildId);
        
        const canalStr = row?.WelcomeChannelId ? `<#${row.WelcomeChannelId}>` : MessagesService.get(guildId, 'Bienvenida:VerNoConfigurado');
        const mensajeStr = row?.WelcomeMessage 
            ? row.WelcomeMessage 
            : `${MessagesService.get(guildId, 'Bienvenida:MensajePorDefecto', { usuario: '{usuario}', servidor: interaction.guild.name })}\n${MessagesService.get(guildId, 'Bienvenida:VerPorDefecto')}`;

        const embed = new EmbedBuilder()
            .setTitle(MessagesService.get(guildId, 'Bienvenida:VerTitulo'))
            .setColor(0x0099FF)
            .addFields(
                { name: MessagesService.get(guildId, 'Bienvenida:VerCanal'), value: canalStr, inline: true },
                { name: MessagesService.get(guildId, 'Bienvenida:VerMensaje'), value: mensajeStr, inline: false }
            );

        await interaction.reply({ embeds: [embed], ephemeral: true });
    }
    else if (subCommand === 'disable') {
        const row = db.prepare(`SELECT WelcomeChannelId FROM GuildConfigs WHERE GuildId = ?`).get(guildId);
        if (!row || !row.WelcomeChannelId) {
            await interaction.reply({
                content: MessagesService.get(guildId, 'Bienvenida:YaDesactivada'),
                ephemeral: true
            });
            return;
        }

        db.prepare(`UPDATE GuildConfigs SET WelcomeChannelId = NULL WHERE GuildId = ?`).run(guildId);
        await interaction.reply({
            content: MessagesService.get(guildId, 'Bienvenida:ConfigDesactivada'),
            ephemeral: true
        });
    }
}
