export function formatDate(dateStr) {
    if (!dateStr) return '';
    return new Date(dateStr).toLocaleDateString('tr-TR', { day: 'numeric', month: 'long', year: 'numeric' });
}

export function formatDateShort(dateStr) {
    if (!dateStr) return '';
    return new Date(dateStr).toLocaleDateString('tr-TR', { day: 'numeric', month: 'long' });
}

export function formatTime(dateStr) {
    if (!dateStr) return '';
    return new Date(dateStr).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' });
}

export function formatSize(bytes) {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / 1048576).toFixed(1) + ' MB';
}

export function getRateLimitMessage(err) {
    const data = err?.response?.data;
    const status = err?.response?.status;
    // Backend'den gelen hata mesajı (standard format: { error: { message } })
    const apiMsg = data?.error?.message || '';

    if (status === 429) {
        // Backend'in 429 mesajı varsa doğrudan gösterilir; yoksa genel bekleme mesajı.
        return apiMsg || 'İstek limiti aşıldı. Lütfen birkaç saniye bekleyin.';
    }
    // LLM sağlayıcısından gelen rate_limit hatası (500 olarak sarılmış)
    if (apiMsg.toLowerCase().includes('rate_limit') || apiMsg.toLowerCase().includes('rate limit')) {
        const retryMatch = apiMsg.match(/Please try again in ([\d.]+\s*\w+)/i);
        const wait = retryMatch ? ` Lütfen ${retryMatch[1]} sonra tekrar deneyin.` : ' Lütfen birkaç saniye bekleyin.';
        return `Sunucu meşgul (AI limiti).${wait}`;
    }
    return apiMsg || 'Sunucuya bağlanılamadı.';
}

// API hatasından kullanıcıya gösterilecek mesajı çözer:
// 429 → backend'in rate-limit mesajı; rate_limit içeren 500 → "Sunucu meşgul (AI limiti)";
// 422 → validation mesajlarını birleştirir; error.message varsa onu, yoksa fallback'i döner.
export function getApiErrorMessage(err, fallback = 'Bir hata oluştu.') {
    if (!err) return fallback;
    const status = err?.response?.status;
    const data = err?.response?.data;
    const apiMsg = data?.error?.message || '';
    const errors = data?.error?.errors;

    if (status === 429) return getRateLimitMessage(err);

    if (apiMsg && (apiMsg.toLowerCase().includes('rate_limit') || apiMsg.toLowerCase().includes('rate limit'))) {
        return getRateLimitMessage(err);
    }

    if (Array.isArray(errors) && errors.length > 0) {
        return errors.join(' ');
    }

    if (apiMsg) return apiMsg;

    // Network / sunucuya ulaşılamıyor
    if (!err.response) return 'Sunucuya bağlanılamadı.';

    return fallback;
}

// Hataya göre toast gösterir: 429/rate-limit için warning, diğerleri için error.
// İptal edilmiş isteklerde hiçbir şey göstermez.
export function showApiError(toast, err, fallback = 'Bir hata oluştu.') {
    if (!err) { toast.error(fallback); return; }
    // Iptal edilmiş istek için toast atma
    if (err?.code === 'ERR_CANCELED' || err?.name === 'CanceledError') return;

    const status = err?.response?.status;
    const apiMsg = err?.response?.data?.error?.message || '';
    const isRateLimit = status === 429
        || apiMsg.toLowerCase().includes('rate_limit')
        || apiMsg.toLowerCase().includes('rate limit');

    const message = getApiErrorMessage(err, fallback);
    if (isRateLimit) toast.warning(message);
    else toast.error(message);
}