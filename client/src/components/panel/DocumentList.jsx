import { useState, useCallback } from 'react';
import { reprocessDocument } from '../../services/api';
import { formatDate, formatSize, departmentLabel } from '../../lib/format';
import SearchInput from '../shared/SearchInput';
import { DocumentSkeleton } from '../shared/Skeleton';
import { useToast } from '../shared/Toast';
import { showApiError } from '../../lib/format';
import Spinner from '../shared/Spinner';
import IconButton from '../shared/IconButton';
import Pagination from '../shared/Pagination';
import DocumentPreviewModal from './DocumentPreviewModal';

function statusLabel(status) {
    const map = {
        Ready: { label: 'Hazır', color: '#4ade80', bg: 'rgba(74,222,128,0.1)' },
        Processing: { label: 'İşleniyor', color: '#fbbf24', bg: 'rgba(251,191,36,0.1)' },
        Failed: { label: 'Hata', color: '#f87171', bg: 'rgba(248,113,113,0.1)' },
        Pending: { label: 'Bekliyor', color: 'var(--text-muted)', bg: 'rgba(148,163,184,0.1)' },
        // Reprocess başarısız ama eski içerik aktif — chat çalışıyor, sadece güncel değil.
        Stale: { label: 'Yeniden işleme başarısız (eski içerik aktif)', color: '#fb923c', bg: 'rgba(251,146,60,0.1)' },
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

export default function DocumentList({
    documents, loading, search, onSearchChange,
    onDelete, onBatchDelete,
    onBatchDownload, onBatchReprocess,
    deletingDocId, onReprocessStart, onReprocess,
    total, page, pageSize, onPageChange,
}) {
    const [previewDoc, setPreviewDoc] = useState(null);
    const [reprocessingIds, setReprocessingIds] = useState(new Set());
    const [selectMode, setSelectMode] = useState(false);
    const [selectedIds, setSelectedIds] = useState(new Set());
    const toast = useToast();

    const selectableDocs = documents.filter(d => d.status !== 'Processing' && d.status !== 'Pending');
    const allSelected = selectableDocs.length > 0 && selectableDocs.every(d => selectedIds.has(d.id));

    const toggleSelect = useCallback((id) => {
        setSelectedIds(prev => {
            const next = new Set(prev);
            if (next.has(id)) next.delete(id); else next.add(id);
            return next;
        });
    }, []);

    const toggleSelectAll = useCallback(() => {
        setSelectedIds(prev => {
            if (selectableDocs.every(d => prev.has(d.id))) return new Set();
            return new Set(selectableDocs.map(d => d.id));
        });
    }, [selectableDocs]);

    const exitSelectMode = useCallback(() => {
        setSelectMode(false);
        setSelectedIds(new Set());
    }, []);

    const handleBatchDelete = useCallback(() => {
        if (selectedIds.size === 0) return;
        onBatchDelete?.(Array.from(selectedIds), exitSelectMode);
    }, [selectedIds, onBatchDelete, exitSelectMode]);

    const handleBatchDownload = useCallback(() => {
        if (selectedIds.size === 0) return;
        const docs = documents.filter(d => selectedIds.has(d.id));
        onBatchDownload?.(docs);  // exit mode'a düşürme — download'tan sonra liste açık kalsın
    }, [selectedIds, documents, onBatchDownload]);

    const handleBatchReprocess = useCallback(() => {
        if (selectedIds.size === 0) return;
        onBatchReprocess?.(Array.from(selectedIds), exitSelectMode);
    }, [selectedIds, onBatchReprocess, exitSelectMode]);

    const handleReprocess = useCallback(async (doc) => {
        if (reprocessingIds.has(doc.id)) return;
        setReprocessingIds(prev => new Set(prev).add(doc.id));
        onReprocessStart?.(doc.id);
        try {
            await reprocessDocument(doc.id);
            onReprocess?.();
        } catch (e) {
            console.error('Yeniden işleme hatası:', e);
            showApiError(toast, e, 'Yeniden işleme başlatılamadı. Lütfen tekrar deneyin.');
            onReprocess?.();
        } finally {
            setReprocessingIds(prev => { const s = new Set(prev); s.delete(doc.id); return s; });
        }
    }, [reprocessingIds, onReprocessStart, onReprocess, toast]);

    const handlePreview = useCallback((doc) => setPreviewDoc(doc), []);
    const handleClosePreview = useCallback(() => setPreviewDoc(null), []);

    return (
        <>
            <div style={{ borderRadius: '16px', overflow: 'hidden', background: 'rgba(32, 26, 58, 0.55)', border: '1px solid rgba(var(--accent-light-rgb),0.14)', backdropFilter: 'blur(24px) saturate(160%)', WebkitBackdropFilter: 'blur(24px) saturate(160%)', boxShadow: '0 8px 28px -10px rgba(0,0,0,0.4), inset 0 1px 0 rgba(255,255,255,0.04)' }}>
                <div className="panel-list-header" style={{ padding: '16px 20px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', borderBottom: '1px solid rgba(255,255,255,0.06)', flexWrap: 'wrap' }}>
                    {/* Seçim modunda başlık gizlenir; yerine sol tarafta seçim kontrolleri gelir */}
                    {!selectMode ? (
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexShrink: 0 }}>
                            <h2 style={{ fontSize: '16.5px', fontWeight: 700, color: 'var(--text-primary)', margin: 0, letterSpacing: '-0.01em' }}>Yüklü Belgeler</h2>
                            <span style={{ fontSize: '12px', fontWeight: 600, padding: '3px 10px', borderRadius: '999px', background: 'rgba(var(--accent-rgb),0.16)', border: '1px solid rgba(var(--accent-light-rgb),0.25)', color: '#d8ccff', whiteSpace: 'nowrap' }}>{total ?? documents.length} belge</span>
                        </div>
                    ) : (
                        <div className="panel-select-info" style={{ display: 'flex', alignItems: 'center', gap: '10px', flexShrink: 0 }}>
                            <button onClick={toggleSelectAll} className="btn btn-ghost btn-sm" disabled={selectableDocs.length === 0}>
                                {allSelected ? 'Seçimi Kaldır' : 'Tümünü Seç'}
                            </button>
                            <span style={{ fontSize: '13px', color: 'var(--text-secondary)', fontWeight: 600, whiteSpace: 'nowrap' }}>{selectedIds.size} seçili</span>
                        </div>
                    )}
                    <div className={selectMode ? 'panel-select-actions' : 'panel-list-actions'} style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap', flex: '1 1 auto', justifyContent: 'flex-end', minWidth: 0 }}>
                        {selectMode ? (
                            <>
                                {/* İndir */}
                                <button onClick={handleBatchDownload} disabled={selectedIds.size === 0}
                                    title="Seçili belgeleri indir"
                                    style={{ padding: '6px 14px', borderRadius: '8px', fontSize: '13px', fontWeight: 600, background: selectedIds.size === 0 ? 'rgba(var(--accent-light-rgb),0.1)' : 'rgba(var(--accent-light-rgb),0.2)', color: '#c4b5fd', border: '1px solid rgba(var(--accent-light-rgb),0.3)', cursor: selectedIds.size === 0 ? 'not-allowed' : 'pointer', opacity: selectedIds.size === 0 ? 0.5 : 1, display: 'flex', alignItems: 'center', gap: '6px' }}>
                                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                        <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" /><polyline points="7 10 12 15 17 10" /><line x1="12" y1="15" x2="12" y2="3" />
                                    </svg>
                                    İndir
                                </button>
                                {/* Yeniden İşle */}
                                <button onClick={handleBatchReprocess} disabled={selectedIds.size === 0}
                                    title="Seçili belgeleri yeniden işle"
                                    style={{ padding: '6px 14px', borderRadius: '8px', fontSize: '13px', fontWeight: 600, background: selectedIds.size === 0 ? 'rgba(251,191,36,0.1)' : 'rgba(251,191,36,0.2)', color: '#fbbf24', border: '1px solid rgba(251,191,36,0.3)', cursor: selectedIds.size === 0 ? 'not-allowed' : 'pointer', opacity: selectedIds.size === 0 ? 0.5 : 1, display: 'flex', alignItems: 'center', gap: '6px' }}>
                                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                        <polyline points="23 4 23 10 17 10" /><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10" />
                                    </svg>
                                    Yeniden İşle
                                </button>
                                {/* Sil */}
                                <button onClick={handleBatchDelete} disabled={selectedIds.size === 0}
                                    style={{ padding: '6px 14px', borderRadius: '8px', fontSize: '13px', fontWeight: 600, background: selectedIds.size === 0 ? 'rgba(248,113,113,0.1)' : 'rgba(248,113,113,0.2)', color: '#f87171', border: '1px solid rgba(248,113,113,0.3)', cursor: selectedIds.size === 0 ? 'not-allowed' : 'pointer', opacity: selectedIds.size === 0 ? 0.5 : 1, display: 'flex', alignItems: 'center', gap: '6px' }}>
                                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="3 6 5 6 21 6" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" /></svg>
                                    Sil ({selectedIds.size})
                                </button>
                                <button onClick={exitSelectMode}
                                    style={{ padding: '6px 14px', borderRadius: '8px', fontSize: '13px', fontWeight: 600, background: 'rgba(255,255,255,0.08)', color: 'var(--text-secondary)', border: '1px solid rgba(255,255,255,0.18)', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px' }}>
                                    Vazgeç
                                </button>
                            </>
                        ) : (
                            <>
                                <button onClick={() => setSelectMode(true)} className="btn btn-ghost btn-sm" disabled={documents.length === 0}>
                                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={{ marginRight: '4px' }}>
                                        <rect x="2" y="2" width="14" height="14" rx="2" />
                                        <path d="M8 22h12a2 2 0 0 0 2-2V8" />
                                    </svg>
                                    Çoklu Seç
                                </button>
                                <div className="panel-search" style={{ flex: '1 1 180px', maxWidth: '260px', minWidth: 0 }}>
                                    <SearchInput value={search} onChange={onSearchChange} placeholder="Belge ara..." />
                                </div>
                            </>
                        )}
                    </div>
                </div>

                {loading ? <DocumentSkeleton /> : documents.length === 0 ? (
                    <div style={{ textAlign: 'center', padding: '48px 16px' }}>
                        <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="var(--text-muted)" strokeWidth="1.5" style={{ margin: '0 auto 12px', display: 'block', opacity: 0.7 }}>
                            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" />
                        </svg>
                        <p style={{ fontSize: '14px', color: 'var(--gray-light)' }}>{search ? 'Sonuç bulunamadı' : 'Henüz belge yüklenmedi'}</p>
                    </div>
                ) : documents.map((doc) => {
                    const st = statusLabel(doc.status);
                    const fi = fileIcon(doc.contentType, doc.fileName);
                    const isProcessing = doc.status === 'Processing' || doc.status === 'Pending';
                    return (
                        <div key={doc.id} style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: '8px 12px', padding: '14px clamp(12px, 2.5vw, 20px)', borderBottom: '1px solid var(--border)', transition: 'background 0.15s', background: selectMode && selectedIds.has(doc.id) ? 'rgba(var(--accent-rgb),0.08)' : 'transparent' }}
                            onMouseEnter={(e) => { if (!(selectMode && selectedIds.has(doc.id))) e.currentTarget.style.background = 'var(--surface2)'; }}
                            onMouseLeave={(e) => { e.currentTarget.style.background = selectMode && selectedIds.has(doc.id) ? 'rgba(var(--accent-rgb),0.08)' : 'transparent'; }}>
                            {/* flex-basis 240px: ada 240px'ten az yer kalırsa aksiyonlar alt satıra iner — yazı asla harf harf sıkışmaz */}
                            <div style={{ display: 'flex', alignItems: 'center', gap: '12px', minWidth: 0, flex: '1 1 240px' }}>
                                {selectMode && (
                                    <input
                                        type="checkbox"
                                        checked={selectedIds.has(doc.id)}
                                        onChange={() => toggleSelect(doc.id)}
                                        disabled={isProcessing}
                                        title={isProcessing ? 'İşlenen belgeler seçilemez' : ''}
                                        style={{ width: '16px', height: '16px', cursor: isProcessing ? 'not-allowed' : 'pointer', accentColor: 'var(--accent-light)', flexShrink: 0 }}
                                    />
                                )}
                                <div style={{ width: '36px', height: '36px', borderRadius: '8px', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0, background: fi.bg }}>
                                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke={fi.color} strokeWidth="2">
                                        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" />
                                    </svg>
                                </div>
                                <div style={{ minWidth: 0 }}>
                                    <p onClick={() => handlePreview(doc)}
                                        style={{ fontSize: '14px', fontWeight: 500, color: 'var(--text-primary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', margin: 0, cursor: 'pointer' }}
                                        onMouseEnter={(e) => e.currentTarget.style.color = 'var(--accent-light)'}
                                        onMouseLeave={(e) => e.currentTarget.style.color = 'var(--text-primary)'}
                                        title="Önizle">
                                        {doc.fileName}
                                    </p>
                                    <p style={{ fontSize: '12px', color: 'var(--gray-light)', marginTop: '2px' }}>
                                        {doc.departmentName ? `${departmentLabel({ name: doc.departmentName, code: doc.departmentCode })} · ` : ''}{formatSize(doc.fileSizeBytes)} · {formatDate(doc.createdAt)}
                                    </p>
                                    {doc.errorMessage && <p style={{ fontSize: '12px', color: '#f87171', marginTop: '2px' }}>{doc.errorMessage}</p>}
                                    {doc.processingNotes && <p style={{ fontSize: '12px', color: '#fb923c', marginTop: '2px' }}>⚠ {doc.processingNotes}</p>}
                                </div>
                            </div>
                            <div className="panel-row-actions" style={{ display: 'flex', alignItems: 'center', gap: '8px', flexShrink: 0, marginLeft: 'auto' }}>
                                <span className="doc-status-badge" style={{
                                    fontSize: '12px', padding: '3px 10px', borderRadius: '8px', fontWeight: 500,
                                    background: st.bg, color: st.color,
                                    display: 'inline-flex', alignItems: 'center', gap: '6px',
                                    animation: isProcessing ? 'docStatusPulse 1.5s ease-in-out infinite' : 'none'
                                }}>
                                    {isProcessing && <Spinner size={10} />}
                                    {st.label}
                                </span>
                                <IconButton onClick={() => handlePreview(doc)} title="Önizle" hoverColor="var(--accent-light)" hoverBg="rgba(var(--accent-light-rgb),0.1)">
                                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" /><circle cx="12" cy="12" r="3" /></svg>
                                </IconButton>
                                <IconButton
                                    onClick={() => handleReprocess(doc)}
                                    title={isProcessing ? 'Belge zaten işleniyor' : 'Yeniden İşle'}
                                    hoverColor="#fbbf24"
                                    hoverBg="rgba(251,191,36,0.1)"
                                    disabled={isProcessing}>
                                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="23 4 23 10 17 10" /><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10" /></svg>
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
            {!loading && (
                <Pagination page={page} pageSize={pageSize} totalCount={total ?? documents.length} onPageChange={onPageChange} />
            )}
            {previewDoc && <DocumentPreviewModal doc={previewDoc} onClose={handleClosePreview} />}
        </>
    );
}
