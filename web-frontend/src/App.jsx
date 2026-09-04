import { BrowserRouter, Routes, Route } from 'react-router-dom';
import Dashboard from './pages/Dashboard';
import Manage from './pages/Manage';

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<Dashboard />} />
        <Route path="/manage/:guildId" element={<Manage />} />
      </Routes>
    </BrowserRouter>
  );
}
