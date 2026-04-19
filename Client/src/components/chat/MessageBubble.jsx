import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';

function formatTime(dateStr) {
    if (!dateStr) return '';
    return new Date(dateStr).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' });
}

export default function MessageBubble({ msg, copiedId, onCopy }) {
    const isUser = msg.role === 'User';

    return (
        <div className={`flex ${isUser ? 'justify-end' : 'justify-start'}`}>
            {!isUser && (
                <div className="w-8 h-8 rounded-lg flex items-center justify-center flex-shrink-0 mr-3 mt-1"
                    style={{ background: 'var(--accent)' }}>
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
                        <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                    </svg>
                </div>
            )}
            <div className="max-w-2xl">
                <div className="rounded-2xl px-4 py-3 text-sm prose-dark"
                    style={{
                        background: isUser ? 'var(--accent)' : 'var(--surface2)',
                        border: isUser ? 'none' : '1px solid var(--border)',
                        color: isUser ? 'white' : '#e2e8f0',
                        borderTopRightRadius: isUser ? '4px' : '16px',
                        borderTopLeftRadius: !isUser ? '4px' : '16px',
                        lineHeight: '1.7',
                    }}>
                    {!isUser ? (
                        <ReactMarkdown
                            remarkPlugins={[remarkGfm]}
                            components={{
                                table: ({ node, ...props }) => (
                                    <div className="table-wrapper"><table {...props} /></div>
                                ),
                            }}
                        >
                            {msg.content}
                        </ReactMarkdown>
                    ) : msg.content}
                </div>
                {/* Saat + Kopyala */}
                <div className={`flex items-center gap-2 mt-1 ${isUser ? 'justify-end' : 'justify-start'}`}>
                    <span className="text-xs" style={{ color: '#475569' }}>{formatTime(msg.createdAt)}</span>
                    {!isUser && (
                        <button
                            onClick={() => onCopy(msg.content, msg.id)}
                            className="text-xs flex items-center gap-1 transition-all"
                            style={{ color: copiedId === msg.id ? '#4ade80' : '#475569', background: 'none', border: 'none', cursor: 'pointer' }}
                        >
                            {copiedId === msg.id ? (
                                <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                                    <polyline points="20 6 9 17 4 12" />
                                </svg>
                            ) : (
                                <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                    <rect x="9" y="9" width="13" height="13" rx="2" />
                                    <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
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