import { useEffect } from 'react';
import { BrowserRouter, Outlet, Route, Routes, useLocation } from 'react-router-dom';
import { AuthProvider } from './auth';
import { I18nProvider } from './i18n';
import Navbar from './components/Navbar';
import Dashboard from './pages/Dashboard';
import Manage from './pages/Manage';

function Shell() {
    const { pathname } = useLocation();
    const showBack = pathname.startsWith('/manage');

    useEffect(() => {
        document.body.classList.toggle('page-manage', showBack);
        return () => document.body.classList.remove('page-manage');
    }, [showBack]);

    return (
        <>
            <Navbar showBack={showBack} />
            <Outlet />
        </>
    );
}

export default function App() {
    return (
        <I18nProvider>
        <AuthProvider>
            <BrowserRouter>
                <Routes>
                    <Route element={<Shell />}>
                        <Route path="/" element={<Dashboard />} />
                        <Route path="/manage/:guildId" element={<Manage />} />
                    </Route>
                </Routes>
            </BrowserRouter>
        </AuthProvider>
        </I18nProvider>
    );
}
