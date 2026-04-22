// Ortak modal bileşeni
export default function Modal({ title, subtitle, onClose, children, maxWidth = 'max-w-md' }) {
    return (
        <div
            className="fixed inset-0 z-50 flex items-center justify-center p-4"
            style={{ background: 'rgba(0,0,0,0.7)' }}
            onClick={onClose}
        >
            <div
                className={`w-full ${maxWidth} flex flex-col rounded-2xl overflow-hidden`}
                style={{ background: 'var(--surface)', border: '1px solid var(--border)', maxHeight: '85vh' }}
                onClick={(e) => e.stopPropagation()}
            >
                {/* Header */}
                <div className="px-6 py-4 flex items-center justify-between flex-shrink-0"
                    style={{ borderBottom: '1px solid var(--border)' }}>
                    <div>
                        <h3 className="font-semibold text-white text-sm">{title}</h3>
                        {subtitle && <p className="text-xs mt-0.5" style={{ color: 'var(--gray-light)' }}>{subtitle}</p>}
                    </div>
                    <button
                        onClick={onClose}
                        style={{ color: '#64748b', background: 'none', border: 'none', cursor: 'pointer', padding: '6px' }}
                        onMouseEnter={(e) => e.currentTarget.style.color = '#e2e8f0'}
                        onMouseLeave={(e) => e.currentTarget.style.color = '#64748b'}
                    >
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                            <line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" />
                        </svg>
                    </button>
                </div>
                {/* İçerik */}
                <div className="overflow-y-auto flex-1">
                    {children}
                </div>
            </div>
        </div>
    );
}