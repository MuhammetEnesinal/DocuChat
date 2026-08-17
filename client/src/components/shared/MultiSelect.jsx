import { useState, useRef, useEffect } from 'react';

// Çoklu seçim dropdown'u. Native <select multiple> ctrl+click gerektirdiği için kullanılmadı;
// yerine checkbox'lı açılır menü. Dışarı tıklama ve Escape ile kapanır.
export default function MultiSelect({
    options = [],
    selected = [],
    onChange,
    placeholder = 'Seçiniz...',
    emptyText = 'Seçenek yok.',
    summaryNoun = 'öğe',
}) {
    const [open, setOpen] = useState(false);
    const ref = useRef(null);

    useEffect(() => {
        if (!open) return;
        const onDown = (e) => { if (ref.current && !ref.current.contains(e.target)) setOpen(false); };
        const onKey = (e) => { if (e.key === 'Escape') setOpen(false); };
        document.addEventListener('mousedown', onDown);
        document.addEventListener('keydown', onKey);
        return () => {
            document.removeEventListener('mousedown', onDown);
            document.removeEventListener('keydown', onKey);
        };
    }, [open]);

    const toggle = (id) =>
        onChange(selected.includes(id) ? selected.filter(x => x !== id) : [...selected, id]);

    const names = options.filter(o => selected.includes(o.id)).map(o => o.name);
    // 1-2 seçimde adları göster, fazlasında say — buton taşmasın.
    const label = names.length === 0
        ? placeholder
        : names.length <= 2 ? names.join(', ') : `${names.length} ${summaryNoun} seçili`;

    return (
        <div ref={ref} style={{ position: 'relative' }}>
            <button
                type="button"
                onClick={() => setOpen(o => !o)}
                disabled={options.length === 0}
                style={{
                    width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '8px',
                    padding: '12px 16px', borderRadius: '10px', fontSize: '14px', textAlign: 'left',
                    background: 'var(--surface2)', border: `1px solid ${open ? 'var(--accent)' : 'var(--border)'}`,
                    color: names.length ? 'var(--text-primary)' : 'var(--gray-light)',
                    cursor: options.length === 0 ? 'not-allowed' : 'pointer', boxSizing: 'border-box',
                }}>
                <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{label}</span>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"
                    style={{ flexShrink: 0, transform: open ? 'rotate(180deg)' : 'none', transition: 'transform 0.15s' }}>
                    <polyline points="6 9 12 15 18 9" />
                </svg>
            </button>

            {open && (
                <div style={{
                    position: 'absolute', top: 'calc(100% + 4px)', left: 0, right: 0, zIndex: 50,
                    maxHeight: '220px', overflowY: 'auto', borderRadius: '10px', padding: '4px',
                    background: 'var(--navy-light, #1c2034)', border: '1px solid var(--border)',
                    boxShadow: '0 12px 32px -8px rgba(0,0,0,0.6)',
                }}>
                    {options.length === 0 ? (
                        <p style={{ fontSize: '13px', color: 'var(--gray-light)', padding: '10px 12px', margin: 0 }}>{emptyText}</p>
                    ) : options.map((o) => {
                        const checked = selected.includes(o.id);
                        return (
                            <div
                                key={o.id}
                                onClick={() => toggle(o.id)}
                                style={{
                                    display: 'flex', alignItems: 'center', gap: '10px', padding: '9px 12px',
                                    borderRadius: '8px', cursor: 'pointer', fontSize: '14px',
                                    background: checked ? 'rgba(var(--accent-rgb),0.16)' : 'transparent',
                                    color: checked ? '#d8ccff' : 'var(--text-primary)',
                                }}
                                onMouseEnter={(e) => { if (!checked) e.currentTarget.style.background = 'rgba(255,255,255,0.06)'; }}
                                onMouseLeave={(e) => { if (!checked) e.currentTarget.style.background = 'transparent'; }}>
                                <input
                                    type="checkbox"
                                    checked={checked}
                                    readOnly
                                    tabIndex={-1}
                                    style={{ width: '15px', height: '15px', accentColor: 'var(--accent)', pointerEvents: 'none', flexShrink: 0 }}
                                />
                                <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{o.name}</span>
                            </div>
                        );
                    })}
                </div>
            )}
        </div>
    );
}
