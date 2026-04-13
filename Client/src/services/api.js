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
        if (err.response?.status === 401) {
            localStorage.removeItem('token');
            window.location.href = '/login';
        }
        return Promise.reject(err);
    }
);

export default api;

// Auth
export const login = (email, password) =>
    api.post('/auth/login', { email, password });

// Chat
export const askQuestion = (question, sessionId = null) =>
    api.post('/chat/ask', { question, sessionId });

export const getSessions = () => api.get('/chat/sessions');
export const getMessages = (sessionId) => api.get(`/chat/sessions/${sessionId}/messages`);
export const deleteSession = (sessionId) => api.delete(`/chat/sessions/${sessionId}`);

// Documents
export const getDocuments = () => api.get('/documents');
export const uploadDocument = (file) => {
    const form = new FormData();
    form.append('file', file);
    return api.post('/documents/upload', form, {
        headers: { 'Content-Type': 'multipart/form-data' },
    });
};
export const deleteDocument = (id) => api.delete(`/documents/${id}`);