import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';

import './css/variables.css';
import './css/base.css';
import './css/layout.css';
import './css/components.css';
import './css/components/buttons.css';
import './css/components/forms.css';
import './css/components/modal.css';
import './css/components/server-card.css';
import './css/components/sidebar.css';
import './css/components/user-pill.css';
import './css/components/home.css';
import './css/components/card-designer.css';
import './css/components/embed-generator.css';

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);
