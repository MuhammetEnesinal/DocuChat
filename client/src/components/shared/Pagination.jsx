// Server-side pagination kontrolü — tablo/kart DIŞINDA, altında gösterilir.
// Tek sayfa varsa (totalPages <= 1) hiç render edilmez.
export default function Pagination({ page, pageSize, totalCount, onPageChange, disabled = false }) {
    const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
    if (totalPages <= 1) return null;

    const go = (p) => {
        if (disabled) return;
        const next = Math.min(totalPages, Math.max(1, p));
        if (next !== page) onPageChange(next);
    };

    // Pencereli sayfa numaraları: 1 … (p-1) p (p+1) … son
    const nums = [];
    const start = Math.max(2, page - 1);
    const end = Math.min(totalPages - 1, page + 1);
    nums.push(1);
    if (start > 2) nums.push('…');
    for (let i = start; i <= end; i++) nums.push(i);
    if (end < totalPages - 1) nums.push('…');
    if (totalPages > 1) nums.push(totalPages);

    const from = (page - 1) * pageSize + 1;
    const to = Math.min(totalCount, page * pageSize);

    const arrowStyle = (enabled) => ({
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        width: '34px', height: '34px', borderRadius: '10px',
        background: 'var(--surface2)', border: '1px solid var(--border)',
        color: enabled ? 'var(--text-secondary)' : 'var(--text-muted)',
        cursor: enabled && !disabled ? 'pointer' : 'not-allowed',
        opacity: enabled && !disabled ? 1 : 0.4, flexShrink: 0,
        transition: 'background 0.15s, border-color 0.15s',
    });

    const pageBtnStyle = (active) => ({
        minWidth: '34px', height: '34px', padding: '0 10px', borderRadius: '10px',
        fontSize: '13px', fontWeight: active ? 700 : 500,
        background: active ? 'var(--accent)' : 'var(--surface2)',
        color: active ? '#fff' : 'var(--text-secondary)',
        border: `1px solid ${active ? 'transparent' : 'var(--border)'}`,
        boxShadow: active ? '0 6px 18px -6px rgba(var(--accent-rgb),0.6)' : 'none',
        cursor: active || disabled ? 'default' : 'pointer', flexShrink: 0,
        transition: 'background 0.15s, border-color 0.15s',
    });

    return (
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', marginTop: '16px', flexWrap: 'wrap' }}>
            <span style={{ fontSize: '13px', color: 'var(--text-muted)', whiteSpace: 'nowrap' }}>
                {from}–{to} / {totalCount}
            </span>
            <div style={{ display: 'flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
                <button type="button" onClick={() => go(page - 1)} disabled={disabled || page <= 1}
                    aria-label="Önceki sayfa" style={arrowStyle(page > 1)}>
                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
                        <polyline points="15 18 9 12 15 6" />
                    </svg>
                </button>
                {nums.map((n, i) => n === '…' ? (
                    <span key={`e${i}`} style={{ color: 'var(--text-muted)', padding: '0 4px', fontSize: '13px', userSelect: 'none' }}>…</span>
                ) : (
                    <button key={n} type="button" onClick={() => go(n)} disabled={disabled || n === page}
                        style={pageBtnStyle(n === page)}
                        onMouseEnter={(e) => { if (n !== page && !disabled) e.currentTarget.style.borderColor = 'rgba(var(--accent-light-rgb),0.4)'; }}
                        onMouseLeave={(e) => { if (n !== page) e.currentTarget.style.borderColor = 'var(--border)'; }}>
                        {n}
                    </button>
                ))}
                <button type="button" onClick={() => go(page + 1)} disabled={disabled || page >= totalPages}
                    aria-label="Sonraki sayfa" style={arrowStyle(page < totalPages)}>
                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
                        <polyline points="9 18 15 12 9 6" />
                    </svg>
                </button>
            </div>
        </div>
    );
}
