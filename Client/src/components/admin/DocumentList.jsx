import { useState, useCallback } from 'react';
import { reprocessDocument } from '../../services/api';
import { formatDate, formatSize } from '../../utils/format';
import SearchInput from '../shared/SearchInput';
import { DocumentSkeleton } from '../shared/Skeleton';
import { useToast } from '../shared/Toast';
import Spinner from '../shared/Spinner';
import IconButton from '../shared/IconButton';
import DocumentPreviewModal from './DocumentPreviewModal';

function statusLabel(status) {
    const map = {
        Ready: { label: 'Hazır', color: '#4ade80', bg: 'rgba(74,222,128,0.1)' },
        Processing: { label: 'İşleniyor', color: '#fbbf24', bg: 'rgba(251,191,36,0.1)' },
        Failed: { label: 'Hata', color: '#f87171', bg: 'rgba(248,113,113,0.1)' },
        Pending: { label: 'Bekliyor', color: 'var(--text-muted)', bg: 'rgba(148,163,184,0.1)' },
    };
    return map[status] ?? map.Pending;
}

function fileIcon(contentType, fileName) {
    const ext = fileName.split('.').pop().toLowerCase();
    if (contentType === 'application/pdf' || ext === 'pdf')
        return { color: '#f87171', bg: 'rgba(248,113,113,0.1)' };
    if (contentType.includes('word') || ext === 'doc' || ext === 'docx')
        return { color: '#60a5fa', bg: 'rgba(96,165,250,0.1)' };
    if (contentType.includes('sheet') || ext === 'xlsx' || ext === 'csv')
        return { color: '#4ade80', bg: 'rgba(74,222,128,0.1)' };
    return { color: 'var(--text-muted)', bg: 'rgba(148,163,184,0.1)' };
}

export default function DocumentList({ documents, loading, search, onSearchChange, onViewChunks, onDelete, deletingDocId, onReprocessStart, onReprocess }) {
    const [previewDoc, setPreviewDoc] = useState(null);
    const [reprocessingIds, setReprocessingIds] = useState(new Set());
    const toast = useToast();

    const handleReprocess = useCallback(async (doc) => {
        if (reprocessingIds.has(doc.id)) return;
        setReprocessingIds(prev => new Set(prev).add(doc.id));
        onReprocessStart?.(doc.id);
        try {
            await reprocessDocument(doc.id);
            onReprocess?.();
        } catch (e) {
            console.error('Yeniden işleme hatası:', e);
            toast.error('Yeniden işleme başlatılamadı. Lütfen tekrar deneyin.');
            onReprocess?.();
        } finally {
            setReprocessingIds(prev => { const s = new Set(prev); s.delete(doc.id); return s; });
        }
    }, [reprocessingIds, onReprocessStart, onReprocess, toast]);

    const handlePreview = useCallback((doc) => setPreviewDoc(doc), []);
    const handleClosePreview = useCallback(() => setPreviewDoc(null), []);

    return (
        <>
            <div style={{ borderRadius: '16px', overflow: 'hidden', background: 'var(--surface)', border: '1px solid var(--border)' }}>
                <div className="admin-list-header" style={{ padding: '16px 20px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', borderBottom: '1px solid var(--border)', flexWrap: 'wrap' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexShrink: 0 }}>
                        <h2 style={{ fontSize: '15px', fontWeight: 600, color: 'var(--text-primary)', margin: 0 }}>Yuklu Belgeler</h2>
                        <span style={{ fontSize: '13px', color: 'var(--gray-light)' }}>{documents.length} belge</span>
                    </div>
                    <div className="admin-search" style={{ maxWidth: '260px', width: '100%' }}>
                        <SearchInput value={search} onChange={onSearchChange} placeholder="Belge ara..." />
                    </div>
                </div>

                {loading ? <DocumentSkeleton /> : documents.length === 0 ? (
                    <div style={{ textAlign: 'center', padding: '48px 16px' }}>
                        <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="#334155" strokeWidth="1.5" style={{ margin: '0 auto 12px', display: 'block' }}>
                            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" />
                        </svg>
                        <p style={{ fontSize: '14px', color: 'var(--gray-light)' }}>{search ? 'Sonuc bulunamadi' : 'Henuz belge yuklenmedi'}</p>
                    </div>
                ) : documents.map((doc) => {
                    const st = statusLabel(doc.status);
                    const fi = fileIcon(doc.contentType, doc.fileName);
                    const isProcessing = doc.status === 'Processing' || doc.status === 'Pending';
                    return (
                        <div key={doc.id} style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '14px 20px', borderBottom: '1px solid var(--border)', transition: 'background 0.15s' }}
                            onMouseEnter={(e) => e.currentTarget.style.background = 'var(--surface2)'}
                            onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '12px', minWidth: 0 }}>
                                <div style={{ width: '36px', height: '36px', borderRadius: '8px', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0, background: fi.bg }}>
                                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke={fi.color} strokeWidth="2">
                                        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" />
                                    </svg>
                                </div>
                                <div style={{ minWidth: 0 }}>
                                    <p onClick={() => handlePreview(doc)}
                                        style={{ fontSize: '14px', fontWeight: 500, color: 'var(--text-primary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', margin: 0, cursor: 'pointer' }}
                                        onMouseEnter={(e) => e.currentTarget.style.color = '#60a5fa'}
                                        onMouseLeave={(e) => e.currentTarget.style.color = 'var(--text-primary)'}
                                        title="Onizle">
                                        {doc.fileName}
                                    </p>
                                    <p style={{ fontSize: '12px', color: 'var(--gray-light)', marginTop: '2px' }}>
                                        {formatSize(doc.fileSizeBytes)} · {doc.chunkCount} chunk · {formatDate(doc.createdAt)}
                                    </p>
                                    {doc.errorMessage && <p style={{ fontSize: '12px', color: '#f87171', marginTop: '2px' }}>{doc.errorMessage}</p>}
                                </div>
                            </div>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexShrink: 0, marginLeft: '12px' }}>
                                <span style={{ fontSize: '12px', padding: '3px 10px', borderRadius: '8px', fontWeight: 500, background: st.bg, color: st.color }}>{st.label}</span>
                                <IconButton onClick={() => handlePreview(doc)} title="Onizle" hoverColor="#a78bfa" hoverBg="rgba(167,139,250,0.1)">
                                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" /><circle cx="12" cy="12" r="3" /></svg>
                                </IconButton>
                                {doc.status === 'Ready' && (
                                    <IconButton onClick={() => onViewChunks(doc)} title="Chunk goruntule" hoverColor="#93c5fd" hoverBg="rgba(147,197,253,0.1)">
                                        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="8" y1="6" x2="21" y2="6" /><line x1="8" y1="12" x2="21" y2="12" /><line x1="8" y1="18" x2="21" y2="18" /><line x1="3" y1="6" x2="3.01" y2="6" /><line x1="3" y1="12" x2="3.01" y2="12" /><line x1="3" y1="18" x2="3.01" y2="18" /></svg>
                                    </IconButton>
                                )}
                                <IconButton
                                    onClick={() => handleReprocess(doc)}
                                    title="Yeniden İşle"
                                    hoverColor="#fbbf24"
                                    hoverBg="rgba(251,191,36,0.1)"
                                    disabled={reprocessingIds.has(doc.id)}>
                                    {reprocessingIds.has(doc.id)
                                        ? <Spinner size={15} />
                                        : <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="23 4 23 10 17 10" /><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10" /></svg>
                                    }
                                </IconButton>
                                <IconButton
                                    onClick={() => !isProcessing && deletingDocId !== doc.id && onDelete(doc.id, doc.fileName)}
                                    title={isProcessing ? 'Belge işleniyor' : deletingDocId === doc.id ? 'Siliniyor...' : 'Sil'}
                                    hoverColor="#f87171"
                                    hoverBg="rgba(248,113,113,0.1)"
                                    disabled={isProcessing || deletingDocId === doc.id}>
                                    {deletingDocId === doc.id
                                        ? <Spinner size={15} />
                                        : <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="3 6 5 6 21 6" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" /></svg>
                                    }
                                </IconButton>
                            </div>
                        </div>
                    );
                })}
            </div>
            {previewDoc && <DocumentPreviewModal doc={previewDoc} onClose={handleClosePreview} />}
        </>
    );
}
