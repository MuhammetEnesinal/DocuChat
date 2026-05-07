import { useState, useEffect, useCallback, useRef } from 'react';
import api, { reprocessDocument } from '../../services/api';
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

function PreviewModal({ doc, onClose }) {
    const [blobUrl, setBlobUrl] = useState(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const blobUrlRef = useRef(null);

    const ext = doc.fileName.split('.').pop().toLowerCase();
    const isPdf = doc.contentType === 'application/pdf' || ext === 'pdf';
    const isImage = ['png', 'jpg', 'jpeg', 'gif', 'bmp', 'webp'].includes(ext);
    const canPreview = isPdf || isImage;

    // Dosyayi Authorization header ile fetch et, blob URL uret
    useEffect(() => {
        if (!canPreview) { setLoading(false); return; }
        let cancelled = false;
        (async () => {
            try {
                const res = await api.get(`/documents/${doc.id}/preview`, {
                    responseType: 'blob',
                });
                const blob = res.data;
                if (cancelled) return;
                const url = URL.createObjectURL(blob);
                blobUrlRef.current = url;
                setBlobUrl(url);
            } catch (e) {
                if (!cancelled) {
                    const status = e?.response?.status;
                    if (status === 404) setError('Dosya fiziksel olarak bulunamadı. Belgeyi yeniden yükleyin.');
                    else if (status === 401) setError('Bu dosyayı görüntüleme yetkiniz yok.');
                    else setError(`Dosya yüklenemedi (${status ?? e.message})`);
                }
            } finally {
                if (!cancelled) setLoading(false);
            }
        })();
        return () => {
            cancelled = true;
            if (blobUrlRef.current) { URL.revokeObjectURL(blobUrlRef.current); blobUrlRef.current = null; }
        };
    }, [doc.id, canPreview]);

    useEffect(() => {
        const h = (e) => { if (e.key === 'Escape') onClose(); };
        window.addEventListener('keydown', h);
        return () => window.removeEventListener('keydown', h);
    }, [onClose]);

    const handleDownload = async () => {
        try {
            const res = await api.get(`/documents/${doc.id}/preview`, {
                responseType: 'blob',
            });
            const blob = res.data;
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url; a.download = doc.fileName; a.click();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        } catch (e) { console.error('Indirme hatasi:', e); }
    };

    return (
        <div onClick={(e) => { if (e.target === e.currentTarget) onClose(); }} style={{
            position: 'fixed', inset: 0, zIndex: 1000,
            background: 'rgba(0,0,0,0.75)', backdropFilter: 'blur(4px)',
            display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '24px',
        }}>
            <div style={{
                background: 'var(--surface)', border: '1px solid var(--border)',
                borderRadius: '16px', width: '100%', maxWidth: '900px',
                maxHeight: '90vh', display: 'flex', flexDirection: 'column', overflow: 'hidden',
            }}>
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '16px 20px', borderBottom: '1px solid var(--border)', flexShrink: 0 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px', minWidth: 0 }}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#60a5fa" strokeWidth="2">
                            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" />
                        </svg>
                        <span style={{ fontSize: '14px', fontWeight: 600, color: 'var(--text-primary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{doc.fileName}</span>
                        <span style={{ fontSize: '12px', color: 'var(--gray-light)', flexShrink: 0 }}>{formatSize(doc.fileSizeBytes)}</span>
                    </div>
                    <div style={{ display: 'flex', gap: '8px', flexShrink: 0 }}>
                        <button onClick={handleDownload} style={{ padding: '6px 12px', borderRadius: '8px', fontSize: '13px', fontWeight: 500, background: 'rgba(96,165,250,0.1)', color: '#60a5fa', border: '1px solid rgba(96,165,250,0.2)', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px' }}>
                            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" /><polyline points="7 10 12 15 17 10" /><line x1="12" y1="15" x2="12" y2="3" /></svg>
                            Indir
                        </button>
                        <button onClick={onClose} style={{ padding: '6px', borderRadius: '8px', background: 'none', border: '1px solid var(--border)', cursor: 'pointer', color: 'var(--text-muted)', display: 'flex', alignItems: 'center' }}>
                            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
                        </button>
                    </div>
                </div>

                <div style={{ flex: 1, overflow: 'hidden', display: 'flex', minHeight: '400px' }}>
                    {loading ? (
                        <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '12px', color: 'var(--gray-light)' }}>
                            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" style={{ animation: 'spin 1s linear infinite' }}><path d="M21 12a9 9 0 1 1-6.219-8.56" /></svg>
                            Yukleniyor...
                        </div>
                    ) : error ? (
                        <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#f87171', fontSize: '14px' }}>
                            Dosya yuklenemedi: {error}
                        </div>
                    ) : !canPreview ? (
                        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', padding: '48px 24px', gap: '16px' }}>
                            <div style={{ width: '64px', height: '64px', borderRadius: '16px', background: 'rgba(96,165,250,0.1)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="#60a5fa" strokeWidth="1.5"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" /></svg>
                            </div>
                            <div style={{ textAlign: 'center' }}>
                                <p style={{ fontSize: '15px', fontWeight: 600, color: 'var(--text-primary)', marginBottom: '8px' }}>{doc.fileName}</p>
                                <p style={{ fontSize: '13px', color: 'var(--gray-light)', marginBottom: '20px' }}>Bu dosya turu tarayicida onizlenemiyor.</p>
                                <button onClick={handleDownload} style={{ padding: '10px 20px', borderRadius: '10px', fontSize: '14px', fontWeight: 500, background: 'var(--accent)', color: 'var(--text-primary)', border: 'none', cursor: 'pointer', display: 'inline-flex', alignItems: 'center', gap: '8px' }}>
                                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" /><polyline points="7 10 12 15 17 10" /><line x1="12" y1="15" x2="12" y2="3" /></svg>
                                    Indir
                                </button>
                            </div>
                        </div>
                    ) : isPdf ? (
                        <iframe src={blobUrl} style={{ width: '100%', height: '100%', border: 'none', minHeight: '600px' }} title={doc.fileName} />
                    ) : (
                        <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '24px', overflow: 'auto' }}>
                            <img src={blobUrl} alt={doc.fileName} style={{ maxWidth: '100%', maxHeight: '70vh', objectFit: 'contain', borderRadius: '8px' }} />
                        </div>
                    )}
                </div>
            </div>
            <style>{`@keyframes spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }`}</style>
        </div>
    );
}

export default function DocumentList({ documents, loading, search, onSearchChange, onViewChunks, onDelete, onReprocessStart, onReprocess }) {
    const [previewDoc, setPreviewDoc] = useState(null);
    const [reprocessingIds, setReprocessingIds] = useState(new Set());

    const handleReprocess = useCallback(async (doc) => {
        if (reprocessingIds.has(doc.id)) return;
        setReprocessingIds(prev => new Set(prev).add(doc.id));
        onReprocessStart?.(doc.id); // optimistic: status'ü hemen Processing yap
        try {
            await reprocessDocument(doc.id);
            onReprocess?.();
        } catch (e) {
            console.error('Yeniden işleme hatası:', e);
            onReprocess?.(); // hata durumunda da listeni yenile
        } finally {
            setReprocessingIds(prev => { const s = new Set(prev); s.delete(doc.id); return s; });
        }
    }, [reprocessingIds, onReprocessStart, onReprocess]);
    const filtered = documents.filter(d => d.fileName.toLowerCase().includes(search.toLowerCase()));

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

                {loading ? <DocumentSkeleton /> : filtered.length === 0 ? (
                    <div style={{ textAlign: 'center', padding: '48px 16px' }}>
                        <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="#334155" strokeWidth="1.5" style={{ margin: '0 auto 12px', display: 'block' }}>
                            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" />
                        </svg>
                        <p style={{ fontSize: '14px', color: 'var(--gray-light)' }}>{search ? 'Sonuc bulunamadi' : 'Henuz belge yuklenmedi'}</p>
                    </div>
                ) : filtered.map((doc) => {
                    const st = statusLabel(doc.status);
                    const fi = fileIcon(doc.contentType, doc.fileName);
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
                                <button onClick={() => handlePreview(doc)} title="Onizle"
                                    style={{ padding: '6px', borderRadius: '8px', background: 'none', border: 'none', cursor: 'pointer', color: '#64748b' }}
                                    onMouseEnter={(e) => { e.currentTarget.style.color = '#a78bfa'; e.currentTarget.style.background = 'rgba(167,139,250,0.1)'; }}
                                    onMouseLeave={(e) => { e.currentTarget.style.color = '#64748b'; e.currentTarget.style.background = 'transparent'; }}>
                                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" /><circle cx="12" cy="12" r="3" /></svg>
                                </button>
                                {doc.status === 'Ready' && (
                                    <button onClick={() => onViewChunks(doc)} title="Chunk goruntule"
                                        style={{ padding: '6px', borderRadius: '8px', background: 'none', border: 'none', cursor: 'pointer', color: '#64748b' }}
                                        onMouseEnter={(e) => { e.currentTarget.style.color = '#93c5fd'; e.currentTarget.style.background = 'rgba(147,197,253,0.1)'; }}
                                        onMouseLeave={(e) => { e.currentTarget.style.color = '#64748b'; e.currentTarget.style.background = 'transparent'; }}>
                                        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="8" y1="6" x2="21" y2="6" /><line x1="8" y1="12" x2="21" y2="12" /><line x1="8" y1="18" x2="21" y2="18" /><line x1="3" y1="6" x2="3.01" y2="6" /><line x1="3" y1="12" x2="3.01" y2="12" /><line x1="3" y1="18" x2="3.01" y2="18" /></svg>
                                    </button>
                                )}
                                {/* Yeniden İşle */}
                                <button onClick={() => handleReprocess(doc)} title="Yeniden İşle"
                                    disabled={reprocessingIds.has(doc.id)}
                                    style={{ padding: '6px', borderRadius: '8px', background: 'none', border: 'none', cursor: reprocessingIds.has(doc.id) ? 'not-allowed' : 'pointer', color: '#64748b', opacity: reprocessingIds.has(doc.id) ? 0.5 : 1 }}
                                    onMouseEnter={(e) => { if (!reprocessingIds.has(doc.id)) { e.currentTarget.style.color = '#fbbf24'; e.currentTarget.style.background = 'rgba(251,191,36,0.1)'; } }}
                                    onMouseLeave={(e) => { e.currentTarget.style.color = '#64748b'; e.currentTarget.style.background = 'transparent'; }}>
                                    {reprocessingIds.has(doc.id) ? (
                                        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" style={{ animation: 'spin 1s linear infinite' }}><path d="M21 12a9 9 0 1 1-6.219-8.56" /></svg>
                                    ) : (
                                        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="23 4 23 10 17 10" /><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10" /></svg>
                                    )}
                                </button>
                                {/* Sil */}
                                {(() => {
                                    const isProcessing = doc.status === 'Processing' || doc.status === 'Pending';
                                    return (
                                        <button
                                            onClick={() => !isProcessing && onDelete(doc.id, doc.fileName)}
                                            disabled={isProcessing}
                                            title={isProcessing ? 'Belge işleniyor, tamamlanmasını bekleyin' : 'Sil'}
                                            style={{ padding: '6px', borderRadius: '8px', background: 'none', border: 'none', cursor: isProcessing ? 'not-allowed' : 'pointer', color: '#64748b', opacity: isProcessing ? 0.35 : 1 }}
                                            onMouseEnter={(e) => { if (!isProcessing) { e.currentTarget.style.color = '#f87171'; e.currentTarget.style.background = 'rgba(248,113,113,0.1)'; } }}
                                            onMouseLeave={(e) => { e.currentTarget.style.color = '#64748b'; e.currentTarget.style.background = 'transparent'; }}>
                                            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="3 6 5 6 21 6" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" /></svg>
                                        </button>
                                    );
                                })()}
                            </div>
                        </div>
                    );
                })}
            </div>
            {previewDoc && <PreviewModal doc={previewDoc} onClose={handleClosePreview} />}
            <style>{`@keyframes spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }`}</style>
        </>
    );
}