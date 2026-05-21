import axios from 'axios';

const API_BASE = import.meta.env.VITE_API_URL || 'http://localhost:5025';
export { API_BASE };

const api = axios.create({
    baseURL: API_BASE + '/api',
    headers: { 'Content-Type': 'application/json' },
});

api.interceptors.request.use((config) => {
    const token = localStorage.getItem('token');
    if (token) config.headers.Authorization = `Bearer ${token}`;
    return config;
});

api.interceptors.response.use(
    (res) => res,
    (err) => {
        // 401 gelince sadece token varsa yönlendir.
        // sessionStorage flag'i Login.jsx'te "Oturum sona erdi" toast'u tetikler.
        if (err.response?.status === 401 && localStorage.getItem('token')) {
            localStorage.removeItem('token');
            localStorage.removeItem('user');
            sessionStorage.setItem('session_expired', '1');
            window.location.href = '/login';
        }
        return Promise.reject(err);
    }
);

export default api;

// ── Auth ──────────────────────────────────────────────────────────────────
export const login = (email, password) =>
    api.post('/auth/login', { email, password });

export const forgotPassword = (email) =>
    api.post('/auth/forgot-password', { email });

export const resetPassword = (email, token, newPassword) =>
    api.post('/auth/reset-password', { email, token, newPassword });

export const getMe = () => api.get('/auth/me');

export const changePassword = (currentPassword, newPassword) =>
    api.post('/auth/change-password', { currentPassword, newPassword });


// ── Chat ──────────────────────────────────────────────────────────────────
export const askQuestion = (question, sessionId = null, signal = null, skipClarification = false) =>
    api.post('/chat/ask', { question, sessionId, skipClarification }, { signal });

export const getSessions = (params = {}) =>
    api.get('/chat/sessions', { params });

export const getMessages = (sessionId, { page = null, pageSize = 50, signal = null } = {}) =>
    api.get(`/chat/sessions/${sessionId}/messages`, {
        params: page ? { page, pageSize } : undefined,
        signal,
    });

export const renameSession = (sessionId, title) =>
    api.patch(`/chat/sessions/${sessionId}`, { title });

export const deleteSession = (sessionId) =>
    api.delete(`/chat/sessions/${sessionId}`);

export const deleteSessionsBatch = (ids) =>
    api.post('/chat/sessions/batch-delete', { ids });

// ── Documents ─────────────────────────────────────────────────────────────
export const getDocuments = (params = {}) =>
    api.get('/documents', { params });

export const getDocumentChunks = (id) =>
    api.get(`/documents/${id}/chunks`);

export const uploadDocument = (file, onProgress) => {
    const form = new FormData();
    form.append('File', file);
    return api.post('/documents/upload', form, {
        headers: { 'Content-Type': 'multipart/form-data' },
        onUploadProgress: (e) => {
            if (onProgress && e.total)
                onProgress(Math.round((e.loaded * 100) / e.total));
        },
    });
};

export const deleteDocument = (id) =>
    api.delete(`/documents/${id}`);

// Preview endpoint'ini blob olarak çek → indirme için
export const downloadDocument = (id) =>
    api.get(`/documents/${id}/preview`, { responseType: 'blob' });

export const deleteDocumentsBatch = (ids) =>
    api.post('/documents/batch-delete', { ids });

export const reprocessDocument = (id) =>
    api.post(`/documents/${id}/reprocess`);

export const getPopularQuestions = (limit = 6) =>
    api.get(`/chat/popular-questions?limit=${limit}`);

// ── Admin ─────────────────────────────────────────────────────────────────
export const adminGetUsers = () =>
    api.get('/admin/users');

export const adminCreateUser = (fullName, email, password) =>
    api.post('/admin/users', { fullName, email, password });

export const adminUpdateUser = (id, fullName, email, password) =>
    api.put(`/admin/users/${id}`, { fullName, email, password: password || null });

export const adminDeleteUser = (id) =>
    api.delete(`/admin/users/${id}`);
