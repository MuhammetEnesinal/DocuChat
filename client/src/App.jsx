import { lazy, Suspense, useEffect, useRef } from 'react';
import { Routes, Route, Navigate } from 'react-router-dom';
import { ToastProvider, useToast } from './components/shared/Toast';
import { useAuth } from './hooks/useAuth';
import { ErrorBoundary } from './components/shared/ErrorBoundary';
import Spinner from './components/shared/Spinner';
import { startRealtime, stopRealtime, subscribeRealtime, runReconnectHandlers } from './lib/realtime';
import { refreshToken } from './services/api';
import { RealtimeEvents } from './lib/realtimeEvents';
import { roleLabel } from './lib/format';
import useAuthStore from './store/authStore';

// Sessiz token yenileme sonrası kullanıcıya "neyin değiştiğini" söyleyen mesajı üretir.
// Gerçek fark yoksa null döner (no-op admin kaydında toast spam'i olmasın).
function describeAccountChange(prev, fresh) {
    const deptStr = (arr) => (arr || []).map(d => d.name).sort().join(', ');
    const prevD = deptStr(prev.departments);
    const newD = deptStr(fresh.departments);
    if (prevD !== newD)
        return newD ? `Departmanınız güncellendi: ${newD}` : 'Departman atamanız kaldırıldı.';

    const roleStr = (arr) => (arr || []).slice().sort().join(',');
    if (roleStr(prev.roles) !== roleStr(fresh.roles))
        return `Yetkiniz güncellendi: ${(fresh.roles || []).map(roleLabel).join(', ') || '—'}`;

    if ((prev.fullName || '') !== (fresh.fullName || ''))
        return 'Hesap bilgileriniz güncellendi.';

    return null;
}

const Login = lazy(() => import('./pages/Login'));
const Chat = lazy(() => import('./pages/Chat'));
const Management = lazy(() => import('./pages/Management'));
const Profile = lazy(() => import('./pages/Profile'));
const NotFound = lazy(() => import('./pages/NotFound'));
const ForgotPassword = lazy(() => import('./pages/ForgotPassword'));
const ResetPassword = lazy(() => import('./pages/ResetPassword'));

function PrivateRoute({ children, requiredRole }) {
    const { isAuthenticated, isAdmin, isManager } = useAuth();
    if (!isAuthenticated) return <Navigate to="/login" replace />;
    // /management: admin VEYA yönetici erişebilir (yönetici içeride yalnız Belgeler sekmesini görür).
    if (requiredRole === 'AdminOrManager' && !isAdmin && !isManager)
        return <Navigate to="/chat" replace />;
    if (requiredRole === 'Admin' && !isAdmin) return <Navigate to="/chat" replace />;
    return children;
}

function GuestRoute({ children }) {
    const { isAuthenticated, homeRoute } = useAuth();
    if (isAuthenticated) return <Navigate to={homeRoute} replace />;
    return children;
}

// Kök yol: girişli kullanıcıyı rolüne göre ana sayfasına (homeRoute), girişsizi login'e yollar.
// Bu route olmadan "/" hiçbir path'e uymaz ve catch-all üzerinden /404'e düşer.
function RootRedirect() {
    const { isAuthenticated, homeRoute } = useAuth();
    return <Navigate to={isAuthenticated ? homeRoute : '/login'} replace />;
}

function OfflineDetector() {
    const toast = useToast();

    useEffect(() => {
        const handleOffline = () =>
            toast.warning('İnternet bağlantısı kesildi. Lütfen bağlantınızı kontrol edin.');
        const handleOnline = () =>
            toast.success('İnternet bağlantısı yeniden kuruldu.');

        window.addEventListener('offline', handleOffline);
        window.addEventListener('online', handleOnline);
        return () => {
            window.removeEventListener('offline', handleOffline);
            window.removeEventListener('online', handleOnline);
        };
    }, [toast]);

    return null;
}

// Gerçek zamanlı (SignalR) bağlantısının yaşam döngüsü: kimlik doğrulanınca başlar, çıkışta durur.
// Tek bağlantı tüm uygulama boyunca paylaşılır; hook'lar lib/realtime üzerinden abone olur.
function RealtimeLifecycle() {
    const { isAuthenticated } = useAuth();
    useEffect(() => {
        if (!isAuthenticated) return undefined;
        startRealtime();
        return () => { stopRealtime(); };
    }, [isAuthenticated]);
    return null;
}

// Sessiz token yenileme dinleyicisi (global — kullanıcı hangi sayfada olursa olsun).
// Admin bu kullanıcının departman/rol/e-postasını değiştirince "user.refresh" sinyali gelir:
//   1. /auth/refresh → DB'den taze claim'lerle yeni token
//   2. authStore'u MERGE ile güncelle (personnelCode/createdAt gibi refresh'te olmayan alanlar korunur)
//      → Profil/management/route-guard'lar kendiliğinden yeni yetkiye göre re-render eder
//   3. SignalR'ı reconnect et → soket yeni departman grubuna girsin (yoksa eski dept sinyali alırdı)
//   4. dept-scoped listeleri (belge/popüler soru) tazele
// Refresh başarısızsa (kullanıcı silindi/kilitli) temiz logout.
function RealtimeAuthRefresh() {
    // toast'ı ref'te tut → effect deps [] kalsın (her render'da yeniden abone olma).
    const toast = useToast();
    const toastRef = useRef(toast);
    toastRef.current = toast;

    useEffect(() => {
        let refreshing = false;
        return subscribeRealtime(async (evt) => {
            // SERT iptal (şifre/e-posta/silme) → anında tüm cihazlardan çıkış.
            if (evt?.type === RealtimeEvents.SessionTerminated) {
                localStorage.removeItem('token');
                localStorage.removeItem('user');
                sessionStorage.setItem('session_expired', '1');
                window.location.href = '/login';
                return;
            }
            // YUMUŞAK iptal (dept/rol) → sessiz token yenileme (kesintisiz).
            if (evt?.type !== RealtimeEvents.UserRefresh || refreshing) return;
            refreshing = true;
            try {
                const res = await refreshToken();
                const { token, ...fresh } = res.data.data;
                const store = useAuthStore.getState();
                const prevUser = store.user || {};
                store.setAuth(token, { ...prevUser, ...fresh });
                await stopRealtime();
                await startRealtime();
                runReconnectHandlers();
                // Kullanıcıya neyin değiştiğini bildir (dept/rol/ad). Gerçek fark yoksa sessiz.
                const msg = describeAccountChange(prevUser, fresh);
                if (msg) (toastRef.current?.info ?? toastRef.current?.success)?.(msg);
            } catch {
                // Kullanıcı silindi/kilitli → oturumu temizle, login'e yönlendir.
                localStorage.removeItem('token');
                localStorage.removeItem('user');
                sessionStorage.setItem('session_expired', '1');
                window.location.href = '/login';
            } finally {
                refreshing = false;
            }
        });
    }, []);
    return null;
}

export default function App() {
    return (
        <ToastProvider>
            <ErrorBoundary>
                <OfflineDetector />
                <RealtimeLifecycle />
                <RealtimeAuthRefresh />
                <Suspense fallback={
                    <div style={{ minHeight: '100vh', display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'var(--navy)' }}>
                        <Spinner size={32} color="var(--accent)" />
                    </div>
                }>
                    <Routes>
                        <Route path="/" element={<RootRedirect />} />
                        <Route path="/login" element={<GuestRoute><Login /></GuestRoute>} />
                        <Route path="/forgot-password" element={<GuestRoute><ForgotPassword /></GuestRoute>} />
                        <Route path="/reset-password" element={<GuestRoute><ResetPassword /></GuestRoute>} />
                        <Route path="/chat" element={<PrivateRoute><Chat /></PrivateRoute>} />
                        <Route path="/profile" element={<PrivateRoute><Profile /></PrivateRoute>} />
                        <Route path="/management" element={<PrivateRoute requiredRole="AdminOrManager"><Management /></PrivateRoute>} />
                        <Route path="/404" element={<NotFound />} />
                        <Route path="*" element={<Navigate to="/404" replace />} />
                    </Routes>
                </Suspense>
            </ErrorBoundary>
        </ToastProvider>
    );
}
