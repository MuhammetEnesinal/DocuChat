import { create } from 'zustand';
import { logout as apiLogout } from '../services/api';

const useAuthStore = create((set) => ({
    token: localStorage.getItem('token') || null,
    user: JSON.parse(localStorage.getItem('user') || 'null'),

    setAuth: (token, user) => {
        localStorage.setItem('token', token);
        localStorage.setItem('user', JSON.stringify(user));
        set({ token, user });
    },

    logout: async () => {
        // Backend auth_token cookie'sini temizle (HttpOnly olduğu için JS'den temizlenemez).
        // Fail olursa sessizce geç — local state temizliği her durumda yapılır.
        try { await apiLogout(); } catch { /* ignore */ }
        localStorage.removeItem('token');
        localStorage.removeItem('user');
        set({ token: null, user: null });
    },
}));

// Cross-tab senkronizasyonu: başka bir sekme token/user değiştirirse bu sekmenin state'ini de güncelle.
// `storage` event yalnızca DİĞER sekmelerde tetiklenir (kendi sekmende değil).
if (typeof window !== 'undefined') {
    window.addEventListener('storage', (e) => {
        if (e.key === 'token') {
            if (e.newValue === null) {
                useAuthStore.setState({ token: null, user: null });
            } else {
                const userRaw = localStorage.getItem('user');
                const user = userRaw ? JSON.parse(userRaw) : null;
                useAuthStore.setState({ token: e.newValue, user });
            }
        } else if (e.key === 'user') {
            useAuthStore.setState({ user: e.newValue ? JSON.parse(e.newValue) : null });
        }
    });
}

export default useAuthStore;