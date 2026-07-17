
import useAuthStore from '../store/authStore';

// authStore'u wrap eden hook — tüm bileşenler bunu kullansın
// localStorage'a direkt erişim yerine buradan
export function useAuth() {
    const { token, user, setAuth, logout } = useAuthStore();
    const isAuthenticated = !!token;
    const isAdmin = user?.roles?.includes('Admin') ?? false;
    const isManager = user?.roles?.includes('Manager') ?? false;
    const departments = user?.departments ?? [];
    // Admin ve yönetici belge yönetimi için /admin'e gider; normal kullanıcı chat'e.
    const homeRoute = (isAdmin || isManager) ? '/admin' : '/chat';
    return { token, user, setAuth, logout, isAuthenticated, isAdmin, isManager, departments, homeRoute };
}