import { Routes, Route, Navigate } from 'react-router-dom';
import useThemeStore from './store/useTheme'; // tema başlangıcı için import et
import { ToastProvider } from './components/shared/Toast';
import { useAuth } from './hooks/useAuth';
import Login from './pages/Login';
import Register from './pages/Register';
import Chat from './pages/Chat';
import Admin from './pages/Admin';
import NotFound from './pages/NotFound';

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

export default function App() {
    useThemeStore(); // tema localStorage'dan yüklensin
    return (
        <ToastProvider>
            <Routes>
                <Route path="/login" element={<GuestRoute><Login /></GuestRoute>} />
                <Route path="/register" element={<GuestRoute><Register /></GuestRoute>} />
                <Route path="/chat" element={<PrivateRoute><Chat /></PrivateRoute>} />
                <Route path="/admin" element={<PrivateRoute requiredRole="Admin"><Admin /></PrivateRoute>} />
                <Route path="/404" element={<NotFound />} />
                <Route path="*" element={<Navigate to="/chat" replace />} />
            </Routes>
        </ToastProvider>
    );
}