import { useRef } from 'react';

export default function ChatInput({ value, onChange, onSend, loading, onAbort, inputRef }) {
    const internalRef = useRef(null);
    const textareaRef = inputRef ?? internalRef;

    const handleKeyDown = (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            onSend();
        }
    };

    const handleInput = (e) => {
        e.target.style.height = 'auto';
        e.target.style.height = e.target.scrollHeight + 'px';
    };

    return (
        <div className="chat-input-area" style={{ padding: 'clamp(8px, 2.5vw, 16px) clamp(10px, 3.5vw, 24px) clamp(10px, 3vw, 20px)', position: 'relative', maxWidth: '900px', margin: '0 auto', width: '100%', minWidth: 0 }}>
            {/* flexWrap + basis 120px: buton yana SIĞMAZSA kutu İÇİNDE alt satıra iner (sağda) —
                butonun kutu dışına taşması hiçbir genişlikte mümkün değil */}
            <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'flex-end', gap: 'clamp(6px, 2vw, 12px)', alignItems: 'flex-end', minWidth: 0, padding: 'clamp(8px, 2.5vw, 14px) clamp(8px, 3vw, 16px)', borderRadius: '20px', background: 'rgba(20, 18, 32, 0.65)', backdropFilter: 'blur(28px) saturate(180%)', WebkitBackdropFilter: 'blur(28px) saturate(180%)', border: '1px solid rgba(var(--accent-light-rgb), 0.18)', boxShadow: '0 20px 60px -20px rgba(var(--accent-rgb), 0.4), inset 0 1px 0 rgba(255,255,255,0.05)' }}>
                <textarea
                    ref={textareaRef}
                    value={value}
                    onChange={(e) => onChange(e.target.value)}
                    onKeyDown={handleKeyDown}
                    onInput={handleInput}
                    placeholder="Belgeler hakkında sorular sorun..."
                    rows={1}
                    maxLength={2000}
                    style={{
                        resize: 'none', minHeight: '48px', maxHeight: '180px',
                        overflowY: 'auto', background: 'transparent', border: 'none',
                        outline: 'none', color: 'var(--text-primary)', fontSize: '0.95rem',
                        // minWidth 0: textarea'nın intrinsic min-genişliği flex kabını taşırmasın,
                        // gönder butonunu kutu dışına itmesin.
                        flex: '1 1 120px', minWidth: 0, padding: '10px 6px', lineHeight: '1.6',
                    }}
                />
                <button onClick={loading ? onAbort : () => onSend()} disabled={!loading && !value.trim()}
                    className={loading ? 'btn btn-icon btn-danger' : (value.trim() ? 'btn btn-icon btn-primary' : 'btn btn-icon btn-secondary')}
                    style={{ width: 'clamp(34px, 10vw, 44px)', height: 'clamp(34px, 10vw, 44px)', borderRadius: '12px', flexShrink: 0 }}>
                    {loading ? (
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="white">
                            <rect x="5" y="5" width="14" height="14" rx="2" />
                        </svg>
                    ) : (
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
                            <line x1="22" y1="2" x2="11" y2="13" /><polygon points="22 2 15 22 11 13 2 9 22 2" />
                        </svg>
                    )}
                </button>
            </div>
            <div style={{ position: 'relative', display: 'flex', justifyContent: 'center', alignItems: 'center', marginTop: '8px' }}>
                <p className="source-hint" style={{ fontSize: '12px', color: 'var(--gray-light)', margin: 0, textAlign: 'center' }}>
                    Enter ile gönder · Shift+Enter yeni satır
                </p>
                {value.length > 1800 && (
                    <span style={{ position: 'absolute', right: 0, fontSize: '11px', color: value.length >= 2000 ? '#ef4444' : '#f59e0b' }}>
                        {2000 - value.length}
                    </span>
                )}
            </div>
        </div>
    );
}