import { EmbedBuilder, PermissionFlagsBits } from 'discord.js';
import { slash } from '../../lib/slash.js';
import { esPt, localize } from '../../lib/localize.js';
import {
    installPalette,
    uninstallPalette,
    removeMemberColor,
    listColors,
    buildChooser
} from '../../services/colorService.js';
import MessagesService from '../../services/messagesService.js';

function sub(builder, name, description, { names, descriptions } = {}) {
    builder.setName(name).setDescription(description);
    localize(builder, { name: names, description: descriptions });
    return builder;
}

export const data = slash('colors', 'Color palette for user names', {
    names: esPt('colores', 'cores'),
    descriptions: esPt(
        'Paleta de colores para los nombres de los usuarios',
        'Paleta de cores para os nomes dos usuários'
    )
})
    .addSubcommand(s => sub(s, 'install', 'Create the color palette roles (admins)', {
        names: esPt('instalar'),
        descriptions: esPt('Crea los roles de la paleta de colores (admins)', 'Cria os cargos da paleta de cores (admins)')
    }).addStringOption(o => o.setName('palette').setDescription('Which palette to install')
        .setNameLocalizations(esPt('paleta'))
        .addChoices({ name: 'Normal', value: 'normal' }, { name: 'Pastel', value: 'pastel' })))
    .addSubcommand(s => sub(s, 'uninstall', 'Remove the palette roles (admins)', {
        names: esPt('desinstalar'),
        descriptions: esPt('Elimina los roles de la paleta (admins)', 'Remove os cargos da paleta (admins)')
    }))
    .addSubcommand(s => sub(s, 'choose', 'Choose your color from the available ones', {
        names: esPt('elegir', 'escolher'),
        descriptions: esPt('Elige tu color entre los disponibles', 'Escolha sua cor entre as disponíveis')
    }))
    .addSubcommand(s => sub(s, 'remove', 'Remove the color you currently have', {
        names: esPt('quitar', 'remover'),
        descriptions: esPt('Quítate el color que tienes puesto', 'Remove a cor que você tem agora')
    }))
    .addSubcommand(s => sub(s, 'list', 'Show the installed colors', {
        names: esPt('listar'),
        descriptions: esPt('Muestra los colores instalados', 'Mostra as cores instaladas')
    }));

export async function execute(interaction) {
    const guildId = interaction.guildId;
    const subName = interaction.options.getSubcommand();
    const admin = new Set(['install', 'uninstall']);

    if (admin.has(subName) && !interaction.member.permissions.has(PermissionFlagsBits.ManageRoles)) {
        return interaction.reply({
            content: MessagesService.get(guildId, 'Errores:SinPermisos'),
            ephemeral: true
        });
    }

    if (subName === 'install') {
        await interaction.deferReply();
        const palette = interaction.options.getString('palette') || 'normal';
        const { created, removed, total } = await installPalette(interaction.guild, palette);
        const key = created === 0 && removed === 0 ? 'Colores:InstalarRepetido' : 'Colores:Instalar';
        return interaction.editReply(MessagesService.get(guildId, key, { paleta: palette, total }));
    }

    if (subName === 'uninstall') {
        await interaction.deferReply();
        const deleted = await uninstallPalette(interaction.guild);
        return interaction.editReply(MessagesService.get(guildId, 'Colores:Desinstalar', { borrados: deleted }));
    }

    if (subName === 'remove') {
        const had = await removeMemberColor(interaction.member, guildId);
        return interaction.reply({
            content: MessagesService.get(guildId, had ? 'Colores:Quitado' : 'Colores:NoTenia'),
            ephemeral: true
        });
    }

    if (subName === 'choose') {
        const payload = buildChooser(interaction.guild);
        if (!payload) {
            return interaction.reply({
                content: MessagesService.get(guildId, 'Colores:NoInstalado'),
                ephemeral: true
            });
        }
        return interaction.reply({ ...payload, ephemeral: true });
    }

    if (subName === 'list') {
        const colors = listColors(guildId);
        const embed = new EmbedBuilder()
            .setTitle(MessagesService.get(guildId, 'Colores:ListarTitulo'))
            .setColor(0x3498DB)
            .setDescription(
                colors.length
                    ? colors.map(c => `• ${c.Name}`).join('  ')
                    : MessagesService.get(guildId, 'Colores:ListarVacios')
            );
        return interaction.reply({ embeds: [embed], ephemeral: true });
    }
}
