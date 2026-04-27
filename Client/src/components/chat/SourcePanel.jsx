const API_BASE = import.meta.env.VITE_API_URL ?? 'http://localhost:5025';

export default function SourcePanel({ chunks }) {
    return (
        <div style={{ width: '288px', flexShrink: 0, overflowY: 'auto', background: 'var(--surface)', borderLeft: '1px solid var(--border)' }}>
            <div style={{ padding: '16px', borderBottom: '1px solid var(--border)' }}>
                <h3 style={{ fontWeight: 600, color: 'white', fontSize: '14px', margin: 0 }}>Kaynak Belgeler</h3>
                <p style={{ fontSize: '12px', marginTop: '4px', color: 'var(--gray-light)' }}>{chunks.length} ilgili bölüm</p>
            </div>
            <div style={{ padding: '12px', display: 'flex', flexDirection: 'column', gap: '12px' }}>
                {chunks.map((chunk, i) => (
                    <div key={i} style={{ borderRadius: '12px', padding: '12px', background: 'var(--surface2)', border: '1px solid var(--border)' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px' }}>
                            <span style={{ fontSize: '11px', padding: '2px 8px', borderRadius: '6px', fontWeight: 500, background: 'rgba(59,130,246,0.15)', color: '#93c5fd', border: '1px solid rgba(59,130,246,0.2)' }}>
                                #{i + 1}
                            </span>
                            <span style={{ fontSize: '12px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', color: '#94a3b8' }}>
                                {chunk.fileName}
                            </span>
                        </div>

                        {chunk.imagePath && (
                            <div style={{ marginBottom: '8px', borderRadius: '8px', overflow: 'hidden', border: '1px solid var(--border)' }}>
                                <img
                                    src={`${API_BASE}/uploads/${chunk.imagePath}`}
                                    alt="Kaynak görsel"
                                    style={{ width: '100%', display: 'block', maxHeight: '200px', objectFit: 'contain', background: '#0f172a' }}
                                    onError={(e) => e.currentTarget.style.display = 'none'}
                                />
                            </div>
                        )}

                        <p style={{ fontSize: '12px', lineHeight: 1.5, color: '#64748b', margin: 0, display: '-webkit-box', WebkitLineClamp: 4, WebkitBoxOrient: 'vertical', overflow: 'hidden' }}>
                            {chunk.content}
                        </p>
                    </div>
                ))}
            </div>
        </div>
    );
}