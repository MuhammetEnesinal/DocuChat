import { lazy, Suspense, useEffect } from 'react';
import { Routes, Route, Navigate } from 'react-router-dom';
import { ToastProvider, useToast } from './components/shared/Toast';
import { useAuth } from './hooks/useAuth';
import { ErrorBoundary } from './components/shared/ErrorBoundary';
import Spinner from './components/shared/Spinner';

const Login = lazy(() => import('./pages/Login'));
const Chat = lazy(() => import('./pages/Chat'));
const ManagementPanel = lazy(() => import('./pages/ManagementPanel'));
const Profile = lazy(() => import('./pages/Profile'));
const NotFound = lazy(() => import('./pages/NotFound'));
const ForgotPassword = lazy(() => import('./pages/ForgotPassword'));
const ResetPassword = lazy(() => import('./pages/ResetPassword'));

function PrivateRoute({ children, requiredRole }) {
    const { isAuthenticated, isAdmin, isManager } = useAuth();
    if (!isAuthenticated) return <Navigate to="/login" replace />;
    // /admin: admin VEYA yönetici erişebilir (yönetici içeride yalnız Belgeler sekmesini görür).
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

export default function App() {
    return (
        <ToastProvider>
            <ErrorBoundary>
                <OfflineDetector />
                <Suspense fallback={
                    <div style={{ minHeight: '100vh', display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'var(--navy)' }}>
                        <Spinner size={32} color="var(--accent)" />
                    </div>
                }>
                    <Routes>
                        <Route path="/login" element={<GuestRoute><Login /></GuestRoute>} />
                        <Route path="/forgot-password" element={<GuestRoute><ForgotPassword /></GuestRoute>} />
                        <Route path="/reset-password" element={<GuestRoute><ResetPassword /></GuestRoute>} />
                        <Route path="/chat" element={<PrivateRoute><Chat /></PrivateRoute>} />
                        <Route path="/profile" element={<PrivateRoute><Profile /></PrivateRoute>} />
                        <Route path="/panel" element={<PrivateRoute requiredRole="AdminOrManager"><ManagementPanel /></PrivateRoute>} />
                        <Route path="/404" element={<NotFound />} />
                        <Route path="*" element={<Navigate to="/404" replace />} />
                    </Routes>
                </Suspense>
            </ErrorBoundary>
        </ToastProvider>
    );
}
