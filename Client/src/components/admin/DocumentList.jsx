import SearchInput from '../shared/SearchInput';
import { DocumentSkeleton } from '../shared/Skeleton';

function formatSize(bytes) {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / 1048576).toFixed(1) + ' MB';
}

function formatDate(dateStr) {
    return new Date(dateStr).toLocaleDateString('tr-TR', { day: 'numeric', month: 'long', year: 'numeric' });
}

function statusLabel(status) {
    const map = {
        Ready: { label: 'Hazır', color: '#4ade80', bg: 'rgba(74,222,128,0.1)' },
        Processing: { label: 'İşleniyor', color: '#fbbf24', bg: 'rgba(251,191,36,0.1)' },
        Failed: { label: 'Hata', color: '#f87171', bg: 'rgba(248,113,113,0.1)' },
        Pending: { label: 'Bekliyor', color: '#94a3b8', bg: 'rgba(148,163,184,0.1)' },
    };
    return map[status] ?? map.Pending;
}

export default function DocumentList({ documents, loading, search, onSearchChange, onViewChunks, onDelete }) {
    const filtered = documents.filter(d => d.fileName.toLowerCase().includes(search.toLowerCase()));

    return (
        <div style={{ borderRadius: '16px', overflow: 'hidden', background: 'var(--surface)', border: '1px solid var(--border)' }}>
            <div className="admin-list-header" style={{ padding: '16px 20px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', borderBottom: '1px solid var(--border)', flexWrap: 'wrap' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexShrink: 0 }}>
                    <h2 style={{ fontSize: '15px', fontWeight: 600, color: 'white', margin: 0 }}>Yüklü Belgeler</h2>
                    <span style={{ fontSize: '13px', color: 'var(--gray-light)' }}>{documents.length} belge</span>
                </div>
                <div className="admin-search" style={{ maxWidth: '260px', width: '100%' }}>
                    <SearchInput value={search} onChange={onSearchChange} placeholder="Belge ara..." />
                </div>
            </div>

            {loading ? <DocumentSkeleton /> : filtered.length === 0 ? (
                <div style={{ textAlign: 'center', padding: '48px 16px' }}>
                    <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="#334155" strokeWidth="1.5" style={{ margin: '0 auto 12px', display: 'block' }}>
                        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" />
                    </svg>
                    <p style={{ fontSize: '14px', color: 'var(--gray-light)' }}>{search ? 'Sonuç bulunamadı' : 'Henüz belge yüklenmedi'}</p>
                </div>
            ) : filtered.map((doc) => {
                const st = statusLabel(doc.status);
                return (
                    <div key={doc.id} style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '14px 20px', borderBottom: '1px solid var(--border)', transition: 'background 0.15s' }}
                        onMouseEnter={(e) => e.currentTarget.style.background = 'var(--surface2)'}
                        onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '12px', minWidth: 0 }}>
                            <div style={{ width: '36px', height: '36px', borderRadius: '8px', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0, background: 'rgba(59,130,246,0.1)' }}>
                                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#3b82f6" strokeWidth="2">
                                    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" />
                                </svg>
                            </div>
                            <div style={{ minWidth: 0 }}>
                                <p style={{ fontSize: '14px', fontWeight: 500, color: 'white', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', margin: 0 }}>{doc.fileName}</p>
                                <p style={{ fontSize: '12px', color: 'var(--gray-light)', marginTop: '2px' }}>
                                    {formatSize(doc.fileSizeBytes)} · {doc.chunkCount} chunk · {formatDate(doc.createdAt)}
                                </p>
                                {doc.errorMessage && <p style={{ fontSize: '12px', color: '#f87171', marginTop: '2px' }}>{doc.errorMessage}</p>}
                            </div>
                        </div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexShrink: 0, marginLeft: '12px' }}>
                            <span className="doc-status-badge" style={{ fontSize: '12px', padding: '3px 10px', borderRadius: '8px', fontWeight: 500, background: st.bg, color: st.color }}>{st.label}</span>
                            {doc.status === 'Ready' && (
                                <button onClick={() => onViewChunks(doc)} title="Chunk görüntüle"
                                    style={{ padding: '6px', borderRadius: '8px', background: 'none', border: 'none', cursor: 'pointer', color: '#64748b' }}
                                    onMouseEnter={(e) => { e.currentTarget.style.color = '#93c5fd'; e.currentTarget.style.background = 'rgba(147,197,253,0.1)'; }}
                                    onMouseLeave={(e) => { e.currentTarget.style.color = '#64748b'; e.currentTarget.style.background = 'transparent'; }}>
                                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                        <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" /><circle cx="12" cy="12" r="3" />
                                    </svg>
                                </button>
                            )}
                            <button onClick={() => onDelete(doc.id, doc.fileName)}
                                style={{ padding: '6px', borderRadius: '8px', background: 'none', border: 'none', cursor: 'pointer', color: '#64748b' }}
                                onMouseEnter={(e) => { e.currentTarget.style.color = '#f87171'; e.currentTarget.style.background = 'rgba(248,113,113,0.1)'; }}
                                onMouseLeave={(e) => { e.currentTarget.style.color = '#64748b'; e.currentTarget.style.background = 'transparent'; }}>
                                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                    <polyline points="3 6 5 6 21 6" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
                                </svg>
                            </button>
                        </div>
                    </div>
                );
            })}
        </div>
    );
}