import { useEffect } from 'react';

export default function ConfirmDialog({ title, message, confirmLabel = 'Sil', confirmDanger = true, onConfirm, onCancel }) {
    useEffect(() => {
        const h = (e) => { if (e.key === 'Escape') onCancel(); };
        window.addEventListener('keydown', h);
        return () => window.removeEventListener('keydown', h);
    }, [onCancel]);

    return (
        <div
            onClick={onCancel}
            style={{ position: 'fixed', inset: 0, zIndex: 1100, background: 'rgba(0,0,0,0.7)', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '24px' }}>
            <div onClick={e => e.stopPropagation()} style={{ background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: '16px', padding: '24px', maxWidth: '400px', width: '100%' }}>
                <h3 style={{ fontSize: '16px', fontWeight: 600, color: 'var(--text-primary)', margin: '0 0 8px' }}>{title}</h3>
                <p style={{ fontSize: '14px', color: 'var(--text-muted)', margin: '0 0 24px' }}>{message}</p>
                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                    <button type="button" onClick={onCancel}
                        style={{ padding: '8px 16px', borderRadius: '8px', background: 'none', border: '1px solid var(--border)', color: 'var(--text-muted)', cursor: 'pointer', fontSize: '14px' }}>
                        İptal
                    </button>
                    <button type="button" onClick={onConfirm}
                        style={{ padding: '8px 16px', borderRadius: '8px', border: 'none', background: confirmDanger ? 'rgba(239,68,68,0.9)' : 'var(--accent)', color: 'white', cursor: 'pointer', fontSize: '14px', fontWeight: 500 }}>
                        {confirmLabel}
                    </button>
                </div>
            </div>
        </div>
    );
}
