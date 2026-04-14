import { Routes, Route, Navigate } from 'react-router-dom';
import useAuthStore from './store/authStore';
import Login from './pages/Login';
import Chat from './pages/Chat';
import Admin from './pages/Admin';

const PrivateRoute = ({ children, requiredRole }) => {
    const token = localStorage.getItem('token');
    if (!token) return <Navigate to="/login" replace />;

    if (requiredRole) {
        const user = JSON.parse(localStorage.getItem('user') || 'null');
        if (!user?.roles?.includes(requiredRole)) return <Navigate to="/chat" replace />;
    }

    return children;
};

export default function App() {
    return (
        <Routes>
            <Route path="/login" element={<Login />} />
            <Route path="/chat" element={<PrivateRoute><Chat /></PrivateRoute>} />
            <Route
                path="/admin"
                element={<PrivateRoute requiredRole="Admin"><Admin /></PrivateRoute>}
            />
            <Route path="*" element={<Navigate to="/chat" replace />} />
        </Routes>
    );
}