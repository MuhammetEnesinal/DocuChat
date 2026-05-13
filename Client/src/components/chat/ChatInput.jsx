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
        <div className="chat-input-area" style={{ padding: '12px 24px 20px' }}>
            <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-end', padding: '10px 12px', borderRadius: '16px', background: 'var(--surface2)', border: '1px solid var(--border)' }}>
                <textarea
                    ref={textareaRef}
                    value={value}
                    onChange={(e) => onChange(e.target.value)}
                    onKeyDown={handleKeyDown}
                    onInput={handleInput}
                    placeholder="Belge hakkında soru sorun... (Enter ile gönder)"
                    rows={1}
                    maxLength={2000}
                    style={{
                        resize: 'none', minHeight: '44px', maxHeight: '160px',
                        overflowY: 'auto', background: 'transparent', border: 'none',
                        outline: 'none', color: 'var(--text-primary)', fontSize: '0.9rem',
                        flex: 1, padding: '8px 4px', lineHeight: '1.6',
                    }}
                />
                <button onClick={loading ? onAbort : () => onSend()} disabled={!loading && !value.trim()}
                    style={{
                        width: '40px', height: '40px', borderRadius: '10px', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0, border: 'none',
                        background: loading ? '#e74c3c' : (value.trim() ? 'var(--accent)' : 'var(--navy-light)'),
                        cursor: loading || value.trim() ? 'pointer' : 'not-allowed', transition: 'background 0.2s',
                    }}>
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
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: '8px' }}>
                <p className="source-hint" style={{ fontSize: '12px', color: 'var(--gray-light)', margin: 0 }}>
                    Enter ile gönder · Shift+Enter yeni satır
                </p>
                {value.length > 1800 && (
                    <span style={{ fontSize: '11px', color: value.length >= 2000 ? '#ef4444' : '#f59e0b' }}>
                        {2000 - value.length}
                    </span>
                )}
            </div>
        </div>
    );
}