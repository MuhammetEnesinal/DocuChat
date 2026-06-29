import { useState, useEffect } from 'react';
import { createPortal } from 'react-dom';

const CATEGORIES = [
    { key: 'wrong_info',   label: 'Yanlış bilgi' },
    { key: 'missing_info', label: 'Eksik bilgi' },
    { key: 'nonsense',     label: 'Anlamsız cevap' },
    { key: 'doc_mismatch', label: 'Belgeyle uyuşmuyor' },
    { key: 'image_issue',  label: 'Görsel yanlış / eksik' },
];

/**
 * Dislike feedback modal — kategori checkbox + serbest metin.
 * - onSubmit: ({ categories, reasonText }) => Promise
 * - onClose: () => void
 * Backend POST /api/chat/feedback contract'ına uyumlu.
 */
export default function FeedbackModal({ open, onSubmit, onClose }) {
    const [categories, setCategories] = useState([]);
    const [reasonText, setReasonText] = useState('');
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState(null);

    useEffect(() => {
        if (open) {
            setCategories([]);
            setReasonText('');
            setError(null);
            setSubmitting(false);
        }
    }, [open]);

    if (!open) return null;

    // Portal: body'e direkt render → chat container'ın stacking context'inden kaçar.
    // Aksi halde header/input gibi parent siblings'i KAPATMAZ (z-index ihlali).
    if (typeof document === 'undefined') return null;

    const toggleCategory = (key) => {
        setCategories(prev =>
            prev.includes(key) ? prev.filter(c => c !== key) : [...prev, key]
        );
    };

    const handleSubmit = async () => {
        setSubmitting(true);
        setError(null);
        try {
            await onSubmit({
                categories: categories.length > 0 ? categories : null,
                reasonText: reasonText.trim() || null,
            });
        } catch (err) {
            const apiMsg = err?.response?.data?.error?.message
                || err?.response?.data?.message
                || err?.message
                || 'Geri bildirim gönderilemedi.';
            setError(apiMsg);
            setSubmitting(false);
        }
    };

    const handleBackdropClick = (e) => {
        if (e.target === e.currentTarget && !submitting) onClose();
    };

    return createPortal((
        <div
            onClick={handleBackdropClick}
            style={{
                position: 'fixed', inset: 0, zIndex: 9999,  // chat input/header her şeyin üzerinde
                // Tam opak siyah + vignette: chat input/blur sızıntısı tamamen maskelenir
                background: 'radial-gradient(ellipse at center, rgba(0,0,0,0.85) 0%, rgba(0,0,0,0.95) 100%)',
                backdropFilter: 'blur(16px) saturate(120%)',
                WebkitBackdropFilter: 'blur(16px) saturate(120%)',
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                padding: '16px',
                animation: 'feedbackBackdropIn 0.2s ease-out',
            }}
        >
            {/* Modal animation keyframes — global inject (component-scoped style tag) */}
            <style>{`
                @keyframes feedbackBackdropIn {
                    from { opacity: 0; }
                    to { opacity: 1; }
                }
                @keyframes feedbackModalIn {
                    from { opacity: 0; transform: scale(0.96) translateY(8px); }
                    to { opacity: 1; transform: scale(1) translateY(0); }
                }
            `}</style>
            <div
                onClick={e => e.stopPropagation()}
                style={{
                    width: '100%', maxWidth: '480px',
                    maxHeight: 'calc(100vh - 32px)',
                    overflowY: 'auto',
                    background: '#161a2b',
                    border: '1px solid rgba(var(--accent-light-rgb),0.35)',
                    borderRadius: '16px',
                    padding: '22px',
                    // Çift gölge: derin shadow + dış mor accent glow → backdrop'tan net ayrılır
                    boxShadow: '0 30px 80px rgba(0,0,0,0.85), 0 0 60px rgba(var(--accent-rgb),0.18), 0 0 0 1px rgba(255,255,255,0.05) inset',
                    color: 'var(--text-primary)',
                    scrollbarWidth: 'thin',
                    scrollbarColor: 'rgba(var(--accent-light-rgb),0.4) transparent',
                    animation: 'feedbackModalIn 0.22s cubic-bezier(0.16, 1, 0.3, 1)',
                    position: 'relative',
                }}
            >
                <h3 style={{ margin: '0 0 4px 0', fontSize: '15px', fontWeight: 600 }}>
                    Cevabı neden beğenmediniz?
                </h3>
                <p style={{ margin: '0 0 12px 0', fontSize: '12.5px', color: 'var(--text-muted)' }}>
                    Geri bildiriminiz yalnızca sizin gelecek cevaplarınızı iyileştirir.
                </p>

                <div style={{ display: 'flex', flexDirection: 'column', gap: '5px', marginBottom: '12px' }}>
                    {CATEGORIES.map(cat => {
                        const checked = categories.includes(cat.key);
                        return (
                            <label
                                key={cat.key}
                                style={{
                                    display: 'flex', alignItems: 'center', gap: '10px',
                                    padding: '7px 11px',
                                    borderRadius: '7px',
                                    background: checked ? 'rgba(var(--accent-light-rgb),0.18)' : '#1f2438',
                                    border: '1px solid ' + (checked ? 'rgba(var(--accent-light-rgb),0.5)' : 'rgba(255,255,255,0.08)'),
                                    cursor: 'pointer',
                                    fontSize: '13px',
                                    transition: 'all 0.15s',
                                }}
                            >
                                <input
                                    type="checkbox"
                                    checked={checked}
                                    onChange={() => toggleCategory(cat.key)}
                                    disabled={submitting}
                                    style={{ accentColor: 'var(--accent-light)' }}
                                />
                                <span>{cat.label}</span>
                            </label>
                        );
                    })}
                </div>

                <label style={{ fontSize: '12.5px', color: 'var(--text-muted)', display: 'block', marginBottom: '6px' }}>
                    Ek açıklama (opsiyonel)
                </label>
                <textarea
                    value={reasonText}
                    onChange={e => setReasonText(e.target.value.slice(0, 500))}
                    disabled={submitting}
                    placeholder="Örn: Belgede 3 ay yazıyor ama 6 ay dedi..."
                    rows={3}
                    style={{
                        width: '100%',
                        background: '#1f2438',
                        border: '1px solid rgba(255,255,255,0.1)',
                        borderRadius: '8px',
                        padding: '10px 12px',
                        color: 'var(--text-primary)',
                        fontSize: '13.5px',
                        resize: 'vertical',
                        fontFamily: 'inherit',
                        boxSizing: 'border-box',
                        outline: 'none',
                    }}
                    onFocus={e => { e.currentTarget.style.borderColor = 'rgba(var(--accent-light-rgb),0.5)'; }}
                    onBlur={e => { e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)'; }}
                />
                <div style={{ fontSize: '11.5px', color: 'var(--text-muted)', textAlign: 'right', marginTop: '4px' }}>
                    {reasonText.length}/500
                </div>

                {error && (
                    <div style={{
                        marginTop: '10px',
                        padding: '8px 12px',
                        borderRadius: '8px',
                        background: 'rgba(239,68,68,0.1)',
                        border: '1px solid rgba(239,68,68,0.3)',
                        color: '#fca5a5',
                        fontSize: '12.5px',
                    }}>
                        {error}
                    </div>
                )}

                <div style={{ display: 'flex', gap: '10px', justifyContent: 'flex-end', marginTop: '14px' }}>
                    <button
                        onClick={onClose}
                        disabled={submitting}
                        style={{
                            padding: '9px 16px',
                            borderRadius: '8px',
                            background: 'transparent',
                            border: '1px solid var(--border)',
                            color: 'var(--text-muted)',
                            cursor: submitting ? 'not-allowed' : 'pointer',
                            fontSize: '13.5px',
                            opacity: submitting ? 0.5 : 1,
                        }}
                    >
                        İptal
                    </button>
                    <button
                        onClick={handleSubmit}
                        disabled={submitting}
                        style={{
                            padding: '9px 16px',
                            borderRadius: '8px',
                            background: 'var(--gradient-accent, linear-gradient(135deg, var(--accent), var(--accent-light)))',
                            border: 'none',
                            color: 'white',
                            cursor: submitting ? 'not-allowed' : 'pointer',
                            fontSize: '13.5px',
                            fontWeight: 600,
                            opacity: submitting ? 0.7 : 1,
                            boxShadow: '0 6px 18px -6px rgba(var(--accent-rgb),0.6)',
                            display: 'inline-flex', alignItems: 'center', gap: '6px',
                        }}
                    >
                        {submitting && (
                            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" className="animate-spin">
                                <path d="M21 12a9 9 0 1 1-6.219-8.56" />
                            </svg>
                        )}
                        {submitting ? 'Gönderiliyor…' : 'Gönder'}
                    </button>
                </div>
            </div>
        </div>
    ), document.body);
}
