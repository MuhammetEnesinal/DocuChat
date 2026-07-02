import { useEffect } from 'react';

export default function Modal({ title, subtitle, onClose, children, maxWidth = 'max-w-md' }) {
    useEffect(() => {
        const h = (e) => { if (e.key === 'Escape') onClose(); };
        window.addEventListener('keydown', h);
        return () => window.removeEventListener('keydown', h);
    }, [onClose]);

    // Modal açıkken body scroll kilitlenir: arka plan kaymaz ve body'nin (modal'ı OYNATMAYAN,
    // ölü görünen) scrollbar'ları kaybolur — tek çalışan scroll modal'ınki kalır.
    useEffect(() => {
        const prev = document.body.style.overflow;
        document.body.style.overflow = 'hidden';
        return () => { document.body.style.overflow = prev; };
    }, []);

    return (
        <div
            className="fixed inset-0 z-50 flex items-center justify-center p-4"
            // overflow auto: modal min-width tabanının altındaki viewport'ta overlay kayar —
            // body min-width'in modal karşılığı (fixed elemanlar body min-width'i görmez)
            style={{ background: 'rgba(0,0,0,0.78)', backdropFilter: 'blur(8px)', WebkitBackdropFilter: 'blur(8px)', overflow: 'auto' }}
            onClick={onClose}
        >
            <div
                className={`w-full ${maxWidth} flex flex-col rounded-2xl overflow-hidden`}
                style={{
                    background: '#161226',
                    border: '1px solid rgba(var(--accent-light-rgb),0.22)',
                    maxHeight: '85vh',
                    minWidth: '280px',   // layout tabanı: bunun altında modal KÜÇÜLMEZ, overlay kayar
                    margin: 'auto',      // taşma durumunda flex-center kırpmasını önler
                    boxShadow: '0 30px 80px -20px rgba(0,0,0,0.7), 0 0 0 1px rgba(255,255,255,0.04), 0 0 40px -10px rgba(var(--accent-rgb),0.25)',
                }}
                onClick={(e) => e.stopPropagation()}
            >
                {/* Header — başlık ortalı, kapat butonu sağda sabit */}
                <div className="px-6 py-4 flex items-center justify-center flex-shrink-0 relative"
                    style={{ borderBottom: '1px solid rgba(255,255,255,0.08)', background: 'rgba(255,255,255,0.02)' }}>
                    <div style={{ textAlign: 'center', minWidth: 0, padding: '0 34px' }}>
                        <h3 style={{ fontSize: '16.5px', fontWeight: 700, color: 'var(--text-primary)', margin: 0, letterSpacing: '-0.01em' }}>{title}</h3>
                        {subtitle && <p className="text-xs mt-0.5" style={{ color: 'var(--gray-light)' }}>{subtitle}</p>}
                    </div>
                    <button
                        onClick={onClose}
                        aria-label="Kapat"
                        style={{ position: 'absolute', right: '14px', top: '50%', transform: 'translateY(-50%)', color: 'var(--text-muted)', background: 'none', border: 'none', cursor: 'pointer', padding: '6px' }}
                        onMouseEnter={(e) => e.currentTarget.style.color = '#e2e8f0'}
                        onMouseLeave={(e) => e.currentTarget.style.color = 'var(--text-muted)'}
                    >
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                            <line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" />
                        </svg>
                    </button>
                </div>
                {/* İçerik — overflowX hidden: içerik taşarsa modal'da gizli yatay scrollbar oluşmasın
                    (içindeki tablolar kendi overflow-x:auto kapsayıcılarında kaydırılır) */}
                <div className="overflow-y-auto flex-1" style={{ overflowX: 'hidden', minWidth: 0 }}>
                    {children}
                </div>
            </div>
        </div>
    );
}