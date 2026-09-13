import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { fetchMe, logout as apiLogout } from './api';

const AuthContext = createContext(null);

export function AuthProvider({ children }) {
    const [user, setUser] = useState(null);
    const [status, setStatus] = useState('loading');

    const refresh = useCallback(async () => {
        try {
            const me = await fetchMe();
            setUser(me);
        } catch (err) {
            if (err.response?.status === 401) setUser(null);
        } finally {
            setStatus('ready');
        }
    }, []);

    useEffect(() => {
        refresh();
    }, [refresh]);

    const logout = useCallback(async () => {
        await apiLogout().catch(() => {});
        setUser(null);
        setStatus('ready');
    }, []);

    const value = useMemo(
        () => ({ user, status, refresh, logout }),
        [user, status, refresh, logout]
    );

    return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
    const ctx = useContext(AuthContext);
    if (!ctx) throw new Error('useAuth must be used within AuthProvider');
    return ctx;
}
