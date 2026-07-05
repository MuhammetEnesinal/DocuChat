import axios from 'axios';

// ?? (|| değil): Docker build'de VITE_API_URL="" verilir → boş string = AYNI ORIGIN
// (nginx /api ve /uploads'ı proxy'ler). Tanımsızsa (lokal dev) localhost fallback'i.
const API_BASE = import.meta.env.VITE_API_URL ?? 'http://localhost:5025';
export { API_BASE };

const api = axios.create({
    baseURL: API_BASE + '/api',
    headers: { 'Content-Type': 'application/json' },
    // Cookie tabanlı JWT için — /uploads/* static dosya isteklerinde tarayıcı auth_token
    // cookie'sini gönderir. CORS tarafında AllowCredentials() açık.
    withCredentials: true,
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

// Backend HttpOnly auth_token cookie'sini temizler (frontend JS bu cookie'yi göremez).
export const logout = () => api.post('/auth/logout');

export const forgotPassword = (email) =>
    api.post('/auth/forgot-password', { email });

export const resetPassword = (email, token, newPassword) =>
    api.post('/auth/reset-password', { email, token, newPassword });

export const getMe = () => api.get('/auth/me');

export const changePassword = (currentPassword, newPassword) =>
    api.post('/auth/change-password', { currentPassword, newPassword });


// ── Chat ──────────────────────────────────────────────────────────────────
// SSE streaming — backend'in chat endpoint'i: /api/chat/ask-stream.
// onEvent her event'te çağrılır.
// Event tipleri: start | cache_hit | clarification | token | complete | done | error
// Dönüş: {ok: true} normal bitti, {ok: false, error} hata, {aborted: true} iptal.
export const askQuestionStream = async (
    { question, sessionId = null, skipClarification = false },
    onEvent,
    signal = null
) => {
    try {
        const response = await fetch(`${API_BASE}/api/chat/ask-stream`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${localStorage.getItem('token')}`,
                'Accept': 'text/event-stream',
            },
            body: JSON.stringify({ question, sessionId, skipClarification }),
            signal,
        });

        if (response.status === 401) {
            localStorage.removeItem('token');
            localStorage.removeItem('user');
            sessionStorage.setItem('session_expired', '1');
            window.location.href = '/login';
            return { ok: false, error: 'Oturum süresi doldu' };
        }

        if (!response.ok) {
            const errText = await response.text().catch(() => '');
            return { ok: false, error: `HTTP ${response.status}: ${errText || response.statusText}` };
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder('utf-8');
        let buffer = '';

        while (true) {
            const { value, done } = await reader.read();
            if (done) break;

            buffer += decoder.decode(value, { stream: true });

            // SSE parsing: "data: <json>\n\n" formatı
            let sepIdx;
            while ((sepIdx = buffer.indexOf('\n\n')) !== -1) {
                const rawEvent = buffer.slice(0, sepIdx);
                buffer = buffer.slice(sepIdx + 2);

                // Her satır "data:" ile başlamalı; payload'ı birleştir
                const dataLines = rawEvent
                    .split('\n')
                    .filter(l => l.startsWith('data:'))
                    .map(l => l.slice(5).replace(/^ /, ''));
                if (dataLines.length === 0) continue;

                const payloadStr = dataLines.join('\n');
                let payload;
                try { payload = JSON.parse(payloadStr); }
                catch { continue; }

                // Debug: streaming sorunlarını teşhis için. Production'da kaldırılabilir.
                if (import.meta.env.DEV) {
                    console.log('[SSE]', payload?.type, payload);
                }
                onEvent(payload);
                if (payload?.type === 'done') return { ok: true };
            }
        }
        return { ok: true };
    } catch (err) {
        if (err.name === 'AbortError') return { aborted: true };
        console.error('[SSE] Stream error:', err);
        return { ok: false, error: err.message || String(err) };
    }
};

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

// ── Session: Arşivleme / Sabitleme / Export ──────────────────────────────
export const archiveSession = (sessionId) =>
    api.patch(`/chat/sessions/${sessionId}/archive`);

export const unarchiveSession = (sessionId) =>
    api.patch(`/chat/sessions/${sessionId}/unarchive`);

export const pinSession = (sessionId) =>
    api.patch(`/chat/sessions/${sessionId}/pin`);

export const unpinSession = (sessionId) =>
    api.patch(`/chat/sessions/${sessionId}/unpin`);

export const getArchivedSessionCount = () =>
    api.get('/chat/sessions/archived-count');

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

// Çoklu reprocess — tek istek, N belge. Backend queue'ya hepsini ekler, consumer throttle yapar.
export const reprocessDocumentsBatch = (ids) =>
    api.post('/documents/batch-reprocess', { ids });

export const getPopularQuestions = (limit = 6) =>
    api.get(`/chat/popular-questions?limit=${limit}`);

// ── Chat Feedback ─────────────────────────────────────────────────────────
// Bir asistan mesajına 👍 / 👎 + sebep gönderir. Sadece bir kez (UNIQUE per user-message).
// rating: 1 (like) | -1 (dislike)
// categories: opsiyonel, whitelist içinden ["wrong_info"|"missing_info"|"nonsense"|"doc_mismatch"|"image_issue"]
// reasonText: opsiyonel, max 500 char serbest metin
export const submitFeedback = ({ messageId, rating, categories = null, reasonText = null }) =>
    api.post('/chat/feedback', { messageId, rating, categories, reasonText });

// ── Admin ─────────────────────────────────────────────────────────────────
export const adminGetUsers = (params = {}) =>
    api.get('/admin/users', { params });

export const adminCreateUser = (fullName, email, personnelCode) =>
    api.post('/admin/users', { fullName, email, personnelCode });

export const adminUpdateUser = (id, fullName, email, password) =>
    api.put(`/admin/users/${id}`, { fullName, email, password: password || null });

export const adminDeleteUser = (id) =>
    api.delete(`/admin/users/${id}`);

// Çoklu kullanıcı silme — tek istek (admin self-delete + son admin koruması serverda).
export const adminDeleteUsersBatch = (ids) =>
    api.post('/admin/users/batch-delete', { ids });

// Excel şablonunu indir (boş template + örnek satır + şifre kuralları)
export const adminDownloadBulkImportTemplate = () =>
    api.get('/admin/users/bulk-import/template', { responseType: 'blob' });

// Excel ile toplu kullanıcı yükleme — SSE streaming.
// Backend her satır işlendikçe progress event yollar. Büyük dosyalarda (2000+ satır)
// "donmuş mu?" hissini engellemek için kullanılır.
//
// Event tipleri: start | progress | done | error
//   start    : { type, total }
//   progress : { type, row, email, status, reason, processed, total }
//   done     : { type, summary: { totalRows, successCount, skippedCount, results } }
//   error    : { type, message }
//
// Dönüş: { ok: true, summary } | { ok: false, error } | { aborted: true }
export const adminBulkImportUsersStream = async (file, onEvent, signal = null) => {
    try {
        const formData = new FormData();
        formData.append('file', file);

        const response = await fetch(`${API_BASE}/api/admin/users/bulk-import/stream`, {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${localStorage.getItem('token')}`,
                'Accept': 'text/event-stream',
                // Content-Type fetch tarafından boundary ile otomatik set edilir
            },
            body: formData,
            signal,
        });

        if (response.status === 401) {
            localStorage.removeItem('token');
            localStorage.removeItem('user');
            sessionStorage.setItem('session_expired', '1');
            window.location.href = '/login';
            return { ok: false, error: 'Oturum süresi doldu' };
        }

        if (!response.ok) {
            const errText = await response.text().catch(() => '');
            return { ok: false, error: `HTTP ${response.status}: ${errText || response.statusText}` };
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder('utf-8');
        let buffer = '';
        let summary = null;
        let errorMessage = null;

        while (true) {
            const { value, done } = await reader.read();
            if (done) break;

            buffer += decoder.decode(value, { stream: true });

            let sepIdx;
            while ((sepIdx = buffer.indexOf('\n\n')) !== -1) {
                const rawEvent = buffer.slice(0, sepIdx);
                buffer = buffer.slice(sepIdx + 2);

                const dataLines = rawEvent
                    .split('\n')
                    .filter(l => l.startsWith('data:'))
                    .map(l => l.slice(5).replace(/^ /, ''));
                if (dataLines.length === 0) continue;

                const payloadStr = dataLines.join('\n');
                let payload;
                try { payload = JSON.parse(payloadStr); }
                catch { continue; }

                onEvent?.(payload);
                if (payload?.type === 'done') summary = payload.summary;
                if (payload?.type === 'error') errorMessage = payload.message;
            }
        }

        if (errorMessage) return { ok: false, error: errorMessage };
        return { ok: true, summary };
    } catch (err) {
        if (err.name === 'AbortError') return { aborted: true };
        console.error('[BulkImport SSE] Stream error:', err);
        return { ok: false, error: err.message || String(err) };
    }
};
