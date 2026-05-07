import { lazy, Suspense, useEffect } from 'react';
import { Routes, Route, Navigate } from 'react-router-dom';
import useThemeStore from './store/useTheme';
import { ToastProvider, useToast } from './components/shared/Toast';
import { useAuth } from './hooks/useAuth';
import { ErrorBoundary } from './components/shared/ErrorBoundary';

const Login = lazy(() => import('./pages/Login'));
const Chat = lazy(() => import('./pages/Chat'));
const Admin = lazy(() => import('./pages/Admin'));
const NotFound = lazy(() => import('./pages/NotFound'));
const ForgotPassword = lazy(() => import('./pages/ForgotPassword'));
const ResetPassword = lazy(() => import('./pages/ResetPassword'));

function PrivateRoute({ children, requiredRole }) {
    const { isAuthenticated, isAdmin } = useAuth();
    if (!isAuthenticated) return <Navigate to="/login" replace />;
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
    }, []);

    return null;
}

export default function App() {
    useThemeStore();
    return (
        <ToastProvider>
            <ErrorBoundary>
                <OfflineDetector />
                <Suspense fallback={<div style={{ minHeight: '100vh', background: 'var(--navy)' }} />}>
                    <Routes>
                        <Route path="/login" element={<GuestRoute><Login /></GuestRoute>} />
                        <Route path="/forgot-password" element={<GuestRoute><ForgotPassword /></GuestRoute>} />
                        <Route path="/reset-password" element={<GuestRoute><ResetPassword /></GuestRoute>} />
                        <Route path="/chat" element={<PrivateRoute><Chat /></PrivateRoute>} />
                        <Route path="/admin" element={<PrivateRoute requiredRole="Admin"><Admin /></PrivateRoute>} />
                        <Route path="/404" element={<NotFound />} />
                        <Route path="*" element={<Navigate to="/chat" replace />} />
                    </Routes>
                </Suspense>
            </ErrorBoundary>
        </ToastProvider>
    );
}
