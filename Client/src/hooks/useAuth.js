
import useAuthStore from '../store/authStore';

// authStore'u wrap eden hook — tüm bileşenler bunu kullansın
// localStorage'a direkt erişim yerine buradan
export function useAuth() {
    const { token, user, setAuth, logout } = useAuthStore();
    const isAuthenticated = !!token;
    const isAdmin = user?.roles?.includes('Admin') ?? false;
    const homeRoute = isAdmin ? '/admin' : '/chat';
    return { token, user, setAuth, logout, isAuthenticated, isAdmin, homeRoute };
}