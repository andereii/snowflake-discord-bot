// CardDesigner - Versión React
import { useState, useEffect, useRef } from 'react';

const FONTS = [
  'Inter', 'Roboto', 'Open Sans', 'Lato', 'Montserrat',
  'Poppins', 'Raleway', 'Nunito', 'Ubuntu', 'Playfair Display'
];

const COLORS = [
  '#FFFFFF', '#E0E0E0', '#FF6B6B', '#FFD93D', '#6BCB77',
  '#4D96FF', '#C77DFF', '#FF9A3D', '#00D9FF', '#FF4D8B'
];

const VARIABLES = [
  { name: '{{username}}', description: 'Nombre del usuario' },
  { name: '{{members}}', description: 'Número de miembros' },
  { name: '{{server}}', description: 'Nombre del servidor' },
  { name: '{{date}}', description: 'Fecha actual' }
];

export default function CardDesigner({ value, onChange, previewUrl }) {
  const [state, setState] = useState({
    font: 'Inter',
    textColor: '#FFFFFF',
    overlayColor: '#000000',
    overlayIntensity: 0.75,
    backgroundBlur: false,
    title: '¡{{username}} ingresó al servidor!',
    subtitle: 'Ahora somos {{members}} miembros',
    backgroundImage: previewUrl || null,
    ...value
  });

  useEffect(() => {
    const link = document.createElement('link');
    link.href = `https://fonts.googleapis.com/css2?family=${state.font.replace(' ', '+')}:wght@400;600;700&display=swap`;
    link.rel = 'stylesheet';
    document.head.appendChild(link);
    return () => link.remove();
  }, [state.font]);

  const update = (patch) => {
    const next = { ...state, ...patch };
    setState(next);
    onChange?.(next);
  };

  return (
    <div className="card-designer">
      <div className="card-designer-preview">
        <div className="card-designer-preview-image"
          style={{
            backgroundImage: state.backgroundImage ? `url(${state.backgroundImage})` : 'none',
            filter: state.backgroundBlur ? 'blur(10px)' : 'none'
          }}
        />
        <div className="card-designer-preview-overlay"
          style={{ backgroundColor: state.overlayColor, opacity: state.overlayIntensity }}
        />
        <div className="card-designer-preview-content">
          <img className="card-designer-preview-avatar" src="https://cdn.discordapp.com/embed/avatars/0.png" alt="Avatar" />
          <div className="card-designer-preview-title" style={{ color: state.textColor, fontFamily: `'${state.font}', sans-serif` }}>
            {state.title}
          </div>
          <div className="card-designer-preview-subtitle" style={{ color: state.textColor, opacity: 0.8, fontFamily: `'${state.font}', sans-serif` }}>
            {state.subtitle}
          </div>
        </div>
      </div>

      <div className="card-designer-section">
        <div className="card-designer-section-title">Estilo de Fuente</div>
        <select className="card-designer-select" value={state.font} onChange={e => update({ font: e.target.value })}>
          {FONTS.map(f => <option key={f} value={f}>{f}</option>)}
        </select>
      </div>

      <div className="card-designer-section">
        <div className="card-designer-section-title">Color del Texto</div>
        <div className="card-designer-color-palette">
          {COLORS.map(c => (
            <div key={c} className={`card-designer-color-swatch ${state.textColor === c ? 'active' : ''}`}
              style={{ background: c }} onClick={() => update({ textColor: c })} />
          ))}
          <input type="color" value={state.textColor} onChange={e => update({ textColor: e.target.value })}
            style={{ width: 32, height: 32, border: 'none', background: 'none', cursor: 'pointer' }} />
        </div>
      </div>

      <div className="card-designer-section">
        <div className="card-designer-section-title">Color de Superposición</div>
        <div className="card-designer-color-palette">
          {COLORS.map(c => (
            <div key={c} className={`card-designer-color-swatch ${state.overlayColor === c ? 'active' : ''}`}
              style={{ background: c }} onClick={() => update({ overlayColor: c })} />
          ))}
          <input type="color" value={state.overlayColor} onChange={e => update({ overlayColor: e.target.value })}
            style={{ width: 32, height: 32, border: 'none', background: 'none', cursor: 'pointer' }} />
        </div>
      </div>

      <div className="card-designer-section">
        <div className="card-designer-section-title">Intensidad de Superposición</div>
        <div className="card-designer-slider-label">
          <span>Intensidad</span>
          <span>{Math.round(state.overlayIntensity * 100)}%</span>
        </div>
        <input type="range" min="0" max="100" value={Math.round(state.overlayIntensity * 100)}
          onChange={e => update({ overlayIntensity: Number(e.target.value) / 100 })} />
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 12 }}>
          <input type="checkbox" checked={state.backgroundBlur} onChange={e => update({ backgroundBlur: e.target.checked })} />
          <span style={{ fontSize: 13, color: 'var(--color-text-muted)' }}>Desenfocar fondo</span>
        </label>
      </div>

      <div className="card-designer-section">
        <div className="card-designer-section-title">Imagen de Fondo</div>
        <label className="card-designer-upload" style={{ cursor: 'pointer' }}>
          <span>🖼️ Subir Imagen</span>
          <input type="file" accept="image/*" hidden onChange={e => {
            const file = e.target.files[0];
            if (!file) return;
            const reader = new FileReader();
            reader.onload = ev => update({ backgroundImage: ev.target.result });
            reader.readAsDataURL(file);
          }} />
        </label>
      </div>

      <div className="card-designer-section">
        <div className="card-designer-section-title">Título de la Tarjeta</div>
        <input type="text" className="card-designer-input" value={state.title} onChange={e => update({ title: e.target.value })} />
      </div>

      <div className="card-designer-section">
        <div className="card-designer-section-title">Subtítulo de la Tarjeta</div>
        <input type="text" className="card-designer-input" value={state.subtitle} onChange={e => update({ subtitle: e.target.value })} />
      </div>
    </div>
  );
}
