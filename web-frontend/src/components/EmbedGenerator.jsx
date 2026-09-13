import { useState } from 'react';

export default function EmbedGenerator({ value, onChange, guildId, context = 'general' }) {
  const [state, setState] = useState({
    author: '', authorIcon: '',
    title: '', description: '',
    color: '#ff4d8b',
    fields: [],
    image: '',
    footer: '', footerIcon: '',
    ...value
  });

  const update = (patch) => {
    const next = { ...state, ...patch };
    setState(next);
    onChange?.(next);
  };

  const VARIABLES = {
    welcome: [{ token: '{{user}}', desc: 'Miembro que ingresó' }, { token: '{{server}}', desc: 'Nombre del servidor' }],
    leave: [{ token: '{{user}}', desc: 'Miembro que se fue' }],
    birthday: [{ token: '{{user}}', desc: 'Miembro que cumple años' }],
    general: [{ token: '{{user}}', desc: 'Miembro' }]
  }[context] ?? [];

  const baseVars = [
    { token: '{{username}}', desc: 'Nombre sin mención' },
    { token: '{{userid}}', desc: 'ID del miembro' },
    { token: '{{members}}', desc: 'Miembros totales' },
    { token: '{{date}}', desc: 'Fecha actual' },
    { token: '{{boost}}', desc: 'Mejoras del servidor' }
  ];

  return (
    <div className="embed-generator">
      <div className="embed-generator-preview">
        <div className="embed-generator-preview-embed" style={{ borderLeftColor: state.color }}>
          {state.author && <div className="embed-generator-preview-author"><span>{state.author}</span></div>}
          {state.title && <div className="embed-generator-preview-title">{state.title}</div>}
          {state.description && <div className="embed-generator-preview-description">{state.description}</div>}
          {state.fields.filter(f => f.name || f.value).map((f, i) => (
            <div key={i} className="embed-generator-preview-field">
              <div className="embed-generator-preview-field-name">{f.name}</div>
              <div className="embed-generator-preview-field-value">{f.value}</div>
            </div>
          ))}
          {state.image && <img className="embed-generator-preview-image" src={state.image} alt="" />}
          {state.footer && <div className="embed-generator-preview-footer"><span>{state.footer}</span></div>}
        </div>
      </div>

      <div className="embed-generator-section">
        <div className="embed-generator-section-title">Autor</div>
        <input className="embed-generator-input" value={state.author} onChange={e => update({ author: e.target.value })} placeholder="Nombre del autor" />
        <input className="embed-generator-input" value={state.authorIcon} onChange={e => update({ authorIcon: e.target.value })} placeholder="URL del icono" />
      </div>

      <div className="embed-generator-section">
        <div className="embed-generator-section-title">Título</div>
        <input className="embed-generator-input" value={state.title} onChange={e => update({ title: e.target.value })} placeholder="Título del embed" />
      </div>

      <div className="embed-generator-section">
        <div className="embed-generator-section-title">Mensaje</div>
        <textarea className="embed-generator-textarea" value={state.description} onChange={e => update({ description: e.target.value })} placeholder="Escribe tu mensaje..." />
        <div className="embed-generator-actions">
          <span className="embed-generator-action-btn" onClick={() => alert('Selector de emojis: implementa popup con /emojis')}}>😊</span>
          <span className="embed-generator-action-btn" onClick={() => alert('Selector de canales: implementa popup con /channels')}}>#</span>
          <span className="embed-generator-action-btn" onClick={() => alert('Selector de roles: implementa popup con /roles')}}>🛡️</span>
          <span className="embed-generator-action-btn" data-vars="true">{'{}'}</span>
        </div>
        <div style={{ fontSize: 11, color: 'var(--color-text-muted)' }}>
          Variables: {[...VARIABLES, ...baseVars].map(v => v.token).join(' ')}
        </div>
      </div>

      <div className="embed-generator-section">
        <label>Color del borde</label>
        <input type="color" value={state.color} onChange={e => update({ color: e.target.value })} />
      </div>

      <div className="embed-generator-section">
        <button className="embed-generator-add-field" onClick={() => update({ fields: [...state.fields, { name: '', value: '', inline: false }] })}>
          + Añadir campo
        </button>
        {state.fields.map((f, i) => (
          <div key={i} className="embed-generator-field">
            <div className="embed-generator-field-header">
              <span>Campo {i + 1}</span>
              <button onClick={() => update({ fields: state.fields.filter((_, j) => j !== i) })}>✕</button>
            </div>
            <input value={f.name} onChange={e => { const n = [...state.fields]; n[i].name = e.target.value; update({ fields: n }); }} placeholder="Nombre" />
            <input value={f.value} onChange={e => { const n = [...state.fields]; n[i].value = e.target.value; update({ fields: n }); }} placeholder="Valor" />
          </div>
        ))}
      </div>

      <div className="embed-generator-section">
        <label className="embed-generator-upload">
          Imagen del embed
          <input type="file" accept="image/*" hidden onChange={e => {
            const file = e.target.files[0]; if (!file) return;
            const r = new FileReader(); r.onload = ev => update({ image: ev.target.result }); r.readAsDataURL(file);
          }} />
        </label>
      </div>

      <div className="embed-generator-section">
        <input value={state.footer} onChange={e => update({ footer: e.target.value })} placeholder="Texto del pie" />
        <input value={state.footerIcon} onChange={e => update({ footerIcon: e.target.value })} placeholder="URL icono del pie" />
      </div>
    </div>
  );
}
