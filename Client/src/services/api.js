import axios from 'axios';

const api = axios.create({
    baseURL: 'http://localhost:5025/api',
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
        // 401 gelince sadece token varsa yönlendir
        // Token yoksa (login sayfasındayız) yönlendirme yapma — hata mesajı göster
        if (err.response?.status === 401 && localStorage.getItem('token')) {
            localStorage.removeItem('token');
            localStorage.removeItem('user');
            window.location.href = '/login';
        }
        return Promise.reject(err);
    }
);

export default api;

// ── Auth ──────────────────────────────────────────────────────────────────
export const login = (email, password) =>
    api.post('/auth/login', { email, password });

export const register = (fullName, email, password) =>
    api.post('/auth/register', { fullName, email, password });

// ── Chat ──────────────────────────────────────────────────────────────────
export const askQuestion = (question, sessionId = null) =>
    api.post('/chat/ask', { question, sessionId });

export const getSessions = () => api.get('/chat/sessions');
export const getMessages = (sessionId) => api.get(`/chat/sessions/${sessionId}/messages`);
export const renameSession = (sessionId, title) =>
    api.patch(`/chat/sessions/${sessionId}`, { title });
export const deleteSession = (sessionId) => api.delete(`/chat/sessions/${sessionId}`);

// ── Documents ─────────────────────────────────────────────────────────────
export const getDocuments = () => api.get('/documents');
export const getDocumentChunks = (id) => api.get(`/documents/${id}/chunks`);
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
export const deleteDocument = (id) => api.delete(`/documents/${id}`);

export const getPopularQuestions = (limit = 6) =>
    api.get(`/chat/popular-questions?limit=${limit}`);

// ── Admin ─────────────────────────────────────────────────────────────────
export const adminGetUsers = () => api.get('/admin/users');
export const adminCreateUser = (fullName, email, password) =>
    api.post('/admin/users', { fullName, email, password });
export const adminDeleteUser = (id) => api.delete(`/admin/users/${id}`);