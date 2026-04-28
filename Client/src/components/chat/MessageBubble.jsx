import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';

const API_BASE = import.meta.env.VITE_API_URL ?? 'http://localhost:5025';

function formatTime(dateStr) {
    if (!dateStr) return '';
    return new Date(dateStr).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' });
}

export default function MessageBubble({ msg, copiedId, onCopy }) {
    const isUser = msg.role === 'User';
    const images = msg.images || [];

    const resolveImgMarker = (text) => {
        const match = text?.match(/^\[IMG:(\d+)\]$/);
        if (!match) return null;
        const idx = parseInt(match[1], 10) - 1;
        return idx >= 0 && idx < images.length ? images[idx] : null;
    };

    const components = {
        table: ({ node, ...props }) => (
            <div className="table-wrapper"><table {...props} /></div>
        ),
        p: ({ node, children, ...props }) => {
            const arr = Array.isArray(children) ? children : [children];
            const processed = arr.flatMap((child, ci) => {
                if (typeof child !== 'string') return [child];
                const parts = child.split(/(\[IMG:\d+\])/g);
                if (parts.length === 1) return [child];
                return parts.map((part, j) => {
                    const imgPath = resolveImgMarker(part.trim());
                    if (imgPath) {
                        return (
                            <span key={`${ci}-${j}`} style={{ display: 'inline-block', margin: '4px 8px', verticalAlign: 'middle' }}>
                                <img
                                    src={`${API_BASE}/uploads/${imgPath}`}
                                    alt="görsel"
                                    style={{ maxWidth: '200px', maxHeight: '150px', objectFit: 'contain', borderRadius: '8px', border: '1px solid var(--border)', display: 'block' }}
                                    onError={(e) => { e.currentTarget.style.display = 'none'; }}
                                />
                            </span>
                        );
                    }
                    return part;
                });
            });
            return <p {...props}>{processed}</p>;
        },
        td: ({ node, children, ...props }) => {
            const text = typeof children === 'string' ? children
                : Array.isArray(children) ? children.map(c => typeof c === 'string' ? c : '').join('') : '';
            const imgPath = resolveImgMarker(text.trim());
            if (imgPath) {
                return (
                    <td {...props}>
                        <img
                            src={`${API_BASE}/uploads/${imgPath}`}
                            alt="görsel"
                            style={{ width: '80px', height: '80px', objectFit: 'contain', borderRadius: '6px', display: 'block' }}
                            onError={(e) => { e.currentTarget.style.display = 'none'; }}
                        />
                    </td>
                );
            }
            return <td {...props}>{children}</td>;
        },
    };

    return (
        <div style={{ display: 'flex', justifyContent: isUser ? 'flex-end' : 'flex-start', alignItems: 'flex-start', gap: '12px' }}>
            {!isUser && (
                <div style={{ width: '32px', height: '32px', borderRadius: '8px', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0, marginTop: '4px', background: 'var(--accent)' }}>
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
                        <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                    </svg>
                </div>
            )}
            <div style={{ maxWidth: '48rem' }}>
                <div className="prose-dark" style={{
                    borderRadius: '16px', padding: '12px 16px', fontSize: '14px',
                    background: isUser ? 'var(--accent)' : 'var(--surface2)',
                    border: isUser ? 'none' : '1px solid var(--border)',
                    color: isUser ? 'white' : '#e2e8f0',
                    borderTopRightRadius: isUser ? '4px' : '16px',
                    borderTopLeftRadius: !isUser ? '4px' : '16px',
                    lineHeight: '1.7',
                }}>
                    {!isUser ? (
                        <ReactMarkdown remarkPlugins={[remarkGfm]} components={components}>
                            {msg.content}
                        </ReactMarkdown>
                    ) : msg.content}
                </div>

                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginTop: '4px', justifyContent: isUser ? 'flex-end' : 'flex-start' }}>
                    <span style={{ fontSize: '12px', color: '#475569' }}>{formatTime(msg.createdAt)}</span>
                    {!isUser && (
                        <button onClick={() => onCopy(msg.content, msg.id)}
                            style={{ fontSize: '12px', display: 'flex', alignItems: 'center', gap: '4px', color: copiedId === msg.id ? '#4ade80' : '#475569', background: 'none', border: 'none', cursor: 'pointer' }}>
                            {copiedId === msg.id ? (
                                <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><polyline points="20 6 9 17 4 12" /></svg>
                            ) : (
                                <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                    <rect x="9" y="9" width="13" height="13" rx="2" /><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
                                </svg>
                            )}
                            {copiedId === msg.id ? 'Kopyalandı' : 'Kopyala'}
                        </button>
                    )}
                </div>
            </div>
        </div>
    );
}