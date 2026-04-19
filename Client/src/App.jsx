import { Routes, Route, Navigate } from 'react-router-dom';
import { ToastProvider } from './components/Toast';
import Login from './pages/Login';
import Register from './pages/Register';
import Chat from './pages/Chat';
import Admin from './pages/Admin';
import NotFound from './pages/NotFound';

const PrivateRoute = ({ children, requiredRole }) => {
    const token = localStorage.getItem('token');
    if (!token) return <Navigate to="/login" replace />;
    if (requiredRole) {
        const user = JSON.parse(localStorage.getItem('user') || 'null');
        if (!user?.roles?.includes(requiredRole)) return <Navigate to="/chat" replace />;
    }
    return children;
};

const GuestRoute = ({ children }) => {
    const token = localStorage.getItem('token');
    if (token) {
        const user = JSON.parse(localStorage.getItem('user') || 'null');
        return <Navigate to={user?.roles?.includes('Admin') ? '/admin' : '/chat'} replace />;
    }
    return children;
};

export default function App() {
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