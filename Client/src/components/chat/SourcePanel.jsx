export default function SourcePanel({ chunks }) {
    return (
        <div className="w-72 flex-shrink-0 overflow-y-auto" style={{ background: 'var(--surface)', borderLeft: '1px solid var(--border)' }}>
            <div className="px-4 py-4" style={{ borderBottom: '1px solid var(--border)' }}>
                <h3 className="font-semibold text-white text-sm">Kaynak Belgeler</h3>
                <p className="text-xs mt-1" style={{ color: 'var(--gray-light)' }}>{chunks.length} ilgili bölüm</p>
            </div>
            <div className="p-3 space-y-3">
                {chunks.map((chunk, i) => (
                    <div key={i} className="rounded-xl p-3" style={{ background: 'var(--surface2)', border: '1px solid var(--border)' }}>
                        <div className="flex items-center gap-2 mb-2">
                            <span className="text-xs px-2 py-0.5 rounded-md font-medium"
                                style={{ background: 'rgba(59,130,246,0.15)', color: '#93c5fd', border: '1px solid rgba(59,130,246,0.2)' }}>
                                #{i + 1}
                            </span>
                            <span className="text-xs truncate" style={{ color: '#94a3b8' }}>{chunk.fileName}</span>
                        </div>
                        <p className="text-xs leading-relaxed"
                            style={{ color: '#64748b', display: '-webkit-box', WebkitLineClamp: 4, WebkitBoxOrient: 'vertical', overflow: 'hidden' }}>
                            {chunk.content}
                        </p>
                    </div>
                ))}
            </div>
        </div>
    );
}