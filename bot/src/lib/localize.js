export function esPt(es, pt = es) {
    return { 'es-ES': es, 'es-419': es, 'pt-BR': pt };
}

export function localize(builder, { name, description } = {}) {
    if (name) builder.setNameLocalizations(typeof name === 'string' ? esPt(name) : name);
    if (description) builder.setDescriptionLocalizations(typeof description === 'string' ? esPt(description) : description);
    return builder;
}
