import { useState, useCallback, useEffect, useRef, useMemo } from 'react';

// ── Toast context & hook ───────────────────────────────────────────────────
import { createContext, useContext } from 'react';

const ToastContext = createContext(null);

export function useToast() {
    const ctx = useContext(ToastContext);
    if (!ctx) throw new Error('useToast must be used inside ToastProvider');
    return ctx;
}

// ── Single Toast item ──────────────────────────────────────────────────────
function ToastItem({ toast, onRemove }) {
    const timerRef = useRef(null);

    useEffect(() => {
        timerRef.current = setTimeout(() => onRemove(toast.id), toast.duration ?? 3500);
        return () => clearTimeout(timerRef.current);
    }, [toast.id, toast.duration, onRemove]);

    const colors = {
        success: { bg: 'rgba(74,222,128,0.12)', border: 'rgba(74,222,128,0.3)', icon: '#4ade80', text: '#86efac' },
        error: { bg: 'rgba(248,113,113,0.12)', border: 'rgba(248,113,113,0.3)', icon: '#f87171', text: '#fca5a5' },
        info: { bg: 'rgba(59,130,246,0.12)', border: 'rgba(59,130,246,0.3)', icon: '#3b82f6', text: '#93c5fd' },
        warning: { bg: 'rgba(251,191,36,0.12)', border: 'rgba(251,191,36,0.3)', icon: '#fbbf24', text: '#fde68a' },
    };

    const c = colors[toast.type] ?? colors.info;

    const icons = {
        success: <polyline points="20 6 9 17 4 12" />,
        error: <><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></>,
        info: <><circle cx="12" cy="12" r="10" /><line x1="12" y1="8" x2="12" y2="12" /><line x1="12" y1="16" x2="12.01" y2="16" /></>,
        warning: <><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" /><line x1="12" y1="9" x2="12" y2="13" /><line x1="12" y1="17" x2="12.01" y2="17" /></>,
    };

    return (
        <div style={{
            display: 'flex', alignItems: 'flex-start', gap: '10px',
            padding: '12px 14px', borderRadius: '12px', marginBottom: '8px',
            background: c.bg, border: `1px solid ${c.border}`,
            boxShadow: '0 4px 16px rgba(0,0,0,0.3)',
            animation: 'slideIn 0.2s ease',
            maxWidth: 'min(340px, calc(100vw - 24px))', minWidth: 'min(240px, calc(100vw - 24px))',
        }}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none"
                stroke={c.icon} strokeWidth="2.5" style={{ flexShrink: 0, marginTop: '1px' }}>
                {icons[toast.type] ?? icons.info}
            </svg>
            <span style={{ fontSize: '0.85rem', color: c.text, flex: 1, lineHeight: '1.5' }}>
                {toast.message}
            </span>
            <button onClick={() => onRemove(toast.id)}
                style={{ background: 'none', border: 'none', cursor: 'pointer', color: c.text, opacity: 0.6, padding: '0 0 0 4px', flexShrink: 0 }}>
                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                    <line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" />
                </svg>
            </button>
        </div>
    );
}

// ── Provider ───────────────────────────────────────────────────────────────
export function ToastProvider({ children }) {
    const [toasts, setToasts] = useState([]);

    const remove = useCallback((id) => {
        setToasts(prev => prev.filter(t => t.id !== id));
    }, []);

    // useMemo: fonksiyon + kısayol metodları BİR KEZ kurulur (render gövdesinde her
    // render'da yeniden atama yapılmaz — React render saflığı korunur).
    const toast = useMemo(() => {
        const fn = (message, type = 'info', duration = 3500) => {
            const id = Date.now() + Math.random();
            setToasts(prev => {
                if (prev.some(t => t.message === message && t.type === type)) return prev;
                const trimmed = prev.length >= 5 ? prev.slice(1) : prev;
                return [...trimmed, { id, message, type, duration }];
            });
        };
        fn.success = (msg, dur) => fn(msg, 'success', dur);
        fn.error = (msg, dur) => fn(msg, 'error', dur);
        fn.info = (msg, dur) => fn(msg, 'info', dur);
        fn.warning = (msg, dur) => fn(msg, 'warning', dur);
        return fn;
    }, []);

    return (
        <ToastContext.Provider value={toast}>
            {children}
            <style>{`@keyframes slideIn { from { opacity:0; transform:translateX(20px); } to { opacity:1; transform:translateX(0); } }`}</style>
            <div style={{
                position: 'fixed', bottom: '24px', right: '24px',
                zIndex: 9999, display: 'flex', flexDirection: 'column-reverse',
            }}>
                {toasts.map(t => (
                    <ToastItem key={t.id} toast={t} onRemove={remove} />
                ))}
            </div>
        </ToastContext.Provider>
    );
}