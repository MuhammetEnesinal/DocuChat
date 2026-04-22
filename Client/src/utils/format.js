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
    const msg = err?.response?.data?.error?.message || '';
    if (err?.response?.status === 429 || msg.includes('rate_limit') || msg.includes('Rate limit')) {
        const waitMatch = msg.match(/Please try again in ([\d.]+[ms])/i);
        const wait = waitMatch ? ` Lütfen ${waitMatch[1]} sonra tekrar deneyin.` : ' Lütfen birkaç saniye bekleyin.';
        return `Sunucu meşgul (token limiti).${wait}`;
    }
    return msg || 'Sunucuya bağlanılamadı.';
}