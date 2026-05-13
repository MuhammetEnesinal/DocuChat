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
        // Kendi API'mizin 429 mesajı varsa doğrudan göster
        if (apiMsg) return apiMsg;
        // Groq/LLM token limit hatası
        const retryMatch = apiMsg.match(/Please try again in ([\d.]+\s*\w+)/i);
        const wait = retryMatch ? ` Lütfen ${retryMatch[1]} sonra tekrar deneyin.` : ' Lütfen birkaç saniye bekleyin.';
        return `İstek limiti aşıldı.${wait}`;
    }
    // LLM sağlayıcısından gelen rate_limit hatası (500 olarak sarılmış)
    if (apiMsg.toLowerCase().includes('rate_limit') || apiMsg.toLowerCase().includes('rate limit')) {
        const retryMatch = apiMsg.match(/Please try again in ([\d.]+\s*\w+)/i);
        const wait = retryMatch ? ` Lütfen ${retryMatch[1]} sonra tekrar deneyin.` : ' Lütfen birkaç saniye bekleyin.';
        return `Sunucu meşgul (AI limiti).${wait}`;
    }
    return apiMsg || 'Sunucuya bağlanılamadı.';
}