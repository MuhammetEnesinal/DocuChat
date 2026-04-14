import { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { getDocuments, uploadDocument, deleteDocument, getDocumentChunks } from '../services/api';

export default function Admin() {
    const [documents, setDocuments] = useState([]);
    const [uploads, setUploads] = useState([]); // { id, name, progress, status, error }
    const [error, setError] = useState('');
    const [search, setSearch] = useState('');
    const [selectedDoc, setSelectedDoc] = useState(null);
    const [chunks, setChunks] = useState([]);
    const [chunksLoading, setChunksLoading] = useState(false);
    const [showChunksModal, setShowChunksModal] = useState(false);
    const [dragOver, setDragOver] = useState(false);
    const fileInputRef = useRef();
    const navigate = useNavigate();

    useEffect(() => { fetchDocs(); }, []);

    const fetchDocs = async () => {
        try {
            const res = await getDocuments();
            setDocuments(res.data.data || []);
        } catch { }
    };

    const processFiles = (files) => {
        const allowed = ['application/pdf',
            'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
            'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
            'text/plain', 'text/csv'];

        const validFiles = Array.from(files).filter(f => allowed.includes(f.type));
        if (validFiles.length === 0) {
            setError('Desteklenmeyen dosya formatı. PDF, DOCX, XLSX, CSV yükleyin.');
            return;
        }
        setError('');
        validFiles.forEach(uploadFile);
    };

    const uploadFile = async (file) => {
        const uploadId = Date.now() + Math.random();
        setUploads(prev => [...prev, { id: uploadId, name: file.name, progress: 0, status: 'uploading' }]);

        try {
            await uploadDocument(file, (progress) => {
                setUploads(prev => prev.map(u => u.id === uploadId ? { ...u, progress } : u));
            });
            setUploads(prev => prev.map(u => u.id === uploadId ? { ...u, progress: 100, status: 'done' } : u));
            fetchDocs();
            setTimeout(() => setUploads(prev => prev.filter(u => u.id !== uploadId)), 3000);
        } catch (err) {
            const msg = err.response?.data?.error?.message || err.message;
            setUploads(prev => prev.map(u => u.id === uploadId ? { ...u, status: 'error', error: msg } : u));
            setTimeout(() => setUploads(prev => prev.filter(u => u.id !== uploadId)), 5000);
        }
    };

    const handleFileInput = (e) => { processFiles(e.target.files); fileInputRef.current.value = ''; };

    const handleDelete = async (id, name) => {
        if (!confirm(`"${name}" silinecek. Emin misiniz?`)) return;
        try {
            await deleteDocument(id);
            setDocuments(prev => prev.filter(d => d.id !== id));
            if (selectedDoc?.id === id) { setSelectedDoc(null); setShowChunksModal(false); }
        } catch { }
    };

    const handleViewChunks = async (doc) => {
        setSelectedDoc(doc);
        setShowChunksModal(true);
        setChunksLoading(true);
        setChunks([]);
        try {
            const res = await getDocumentChunks(doc.id);
            setChunks(res.data.data || []);
        } catch { }
        finally { setChunksLoading(false); }
    };

    const statusLabel = (status) => {
        const map = {
            Ready:      { label: 'Hazır',     color: '#4ade80', bg: 'rgba(74,222,128,0.1)' },
            Processing: { label: 'İşleniyor', color: '#fbbf24', bg: 'rgba(251,191,36,0.1)' },
            Failed:     { label: 'Hata',      color: '#f87171', bg: 'rgba(248,113,113,0.1)' },
            Pending:    { label: 'Bekliyor',  color: '#94a3b8', bg: 'rgba(148,163,184,0.1)' },
        };
        return map[status] || map.Pending;
    };

    const formatSize = (bytes) => {
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
        return (bytes / 1048576).toFixed(1) + ' MB';
    };

    const filtered = documents.filter(d =>
        d.fileName.toLowerCase().includes(search.toLowerCase())
    );

    return (
        <div className="min-h-screen" style={{ background: 'var(--navy)' }}>
            {/* Header */}
            <div className="px-8 py-5 flex items-center justify-between" style={{ background: 'var(--surface)', borderBottom: '1px solid var(--border)' }}>
                <div className="flex items-center gap-4">
                    <button onClick={() => navigate('/chat')}
                        className="flex items-center gap-2 text-sm transition-all px-3 py-2 rounded-lg"
                        style={{ color: '#94a3b8' }}
                        onMouseEnter={(e) => e.currentTarget.style.background = 'var(--surface2)'}
                        onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}>
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                            <line x1="19" y1="12" x2="5" y2="12" /><polyline points="12 19 5 12 12 5" />
                        </svg>
                        Sohbete Dön
                    </button>
                    <div style={{ width: '1px', height: '20px', background: 'var(--border)' }} />
                    <h1 className="text-lg font-bold text-white">Admin Panel</h1>
                </div>
                <span className="text-xs px-3 py-1.5 rounded-lg" style={{ background: 'rgba(59,130,246,0.15)', color: '#93c5fd', border: '1px solid rgba(59,130,246,0.2)' }}>
                    Belge Yönetimi
                </span>
            </div>

            <div className="max-w-5xl mx-auto px-8 py-8">
                {/* Upload Alanı */}
                <div className="mb-8 rounded-2xl p-6" style={{ background: 'var(--surface)', border: '1px solid var(--border)' }}>
                    <h2 className="text-base font-semibold text-white mb-1">Belge Yükle</h2>
                    <p className="text-sm mb-5" style={{ color: 'var(--gray-light)' }}>
                        Birden fazla dosya seçebilir veya sürükleyip bırakabilirsiniz.
                    </p>

                    <div
                        className="rounded-xl p-8 text-center cursor-pointer transition-all"
                        style={{
                            border: `2px dashed ${dragOver ? 'var(--accent)' : 'var(--border)'}`,
                            background: dragOver ? 'rgba(59,130,246,0.05)' : 'var(--surface2)'
                        }}
                        onDragOver={(e) => { e.preventDefault(); setDragOver(true); }}
                        onDragLeave={() => setDragOver(false)}
                        onDrop={(e) => { e.preventDefault(); setDragOver(false); processFiles(e.dataTransfer.files); }}
                        onClick={() => fileInputRef.current.click()}
                    >
                        <div className="w-12 h-12 rounded-xl flex items-center justify-center mx-auto mb-3"
                            style={{ background: 'rgba(59,130,246,0.15)' }}>
                            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#3b82f6" strokeWidth="2">
                                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                                <polyline points="17 8 12 3 7 8" /><line x1="12" y1="3" x2="12" y2="15" />
                            </svg>
                        </div>
                        <p className="text-sm font-medium text-white mb-1">Dosya seçin veya buraya sürükleyin</p>
                        <p className="text-xs" style={{ color: 'var(--gray-light)' }}>PDF, DOCX, XLSX, CSV · Maks. 50 MB · Çoklu seçim desteklenir</p>
                    </div>

                    <input ref={fileInputRef} type="file" accept=".pdf,.docx,.xlsx,.csv" className="hidden" multiple onChange={handleFileInput} />

                    {/* Upload progress listesi */}
                    {uploads.length > 0 && (
                        <div className="mt-4 space-y-2">
                            {uploads.map((u) => (
                                <div key={u.id} className="px-4 py-3 rounded-xl" style={{
                                    background: u.status === 'error' ? 'rgba(239,68,68,0.08)' : 'rgba(59,130,246,0.08)',
                                    border: `1px solid ${u.status === 'error' ? 'rgba(239,68,68,0.2)' : 'rgba(59,130,246,0.2)'}`
                                }}>
                                    <div className="flex items-center justify-between mb-1.5">
                                        <span className="text-xs font-medium truncate max-w-xs" style={{ color: u.status === 'error' ? '#fca5a5' : '#93c5fd' }}>
                                            {u.name}
                                        </span>
                                        <span className="text-xs ml-2 flex-shrink-0" style={{ color: u.status === 'error' ? '#fca5a5' : '#93c5fd' }}>
                                            {u.status === 'error' ? 'Hata' : u.status === 'done' ? '✓ Tamamlandı' : `%${u.progress}`}
                                        </span>
                                    </div>
                                    {u.status === 'uploading' && (
                                        <div className="h-1 rounded-full overflow-hidden" style={{ background: 'rgba(59,130,246,0.2)' }}>
                                            <div className="h-full rounded-full transition-all duration-300"
                                                style={{ width: `${u.progress}%`, background: 'var(--accent)' }} />
                                        </div>
                                    )}
                                    {u.status === 'error' && u.error && (
                                        <p className="text-xs mt-1" style={{ color: '#fca5a5' }}>{u.error}</p>
                                    )}
                                </div>
                            ))}
                        </div>
                    )}

                    {error && (
                        <div className="mt-3 px-4 py-3 rounded-xl text-sm" style={{ background: 'rgba(239,68,68,0.1)', border: '1px solid rgba(239,68,68,0.2)', color: '#fca5a5' }}>
                            {error}
                        </div>
                    )}
                </div>

                {/* Belge Listesi */}
                <div className="rounded-2xl overflow-hidden" style={{ background: 'var(--surface)', border: '1px solid var(--border)' }}>
                    <div className="px-6 py-4 flex items-center justify-between gap-4" style={{ borderBottom: '1px solid var(--border)' }}>
                        <div className="flex items-center gap-3">
                            <h2 className="text-base font-semibold text-white">Yüklü Belgeler</h2>
                            <span className="text-sm" style={{ color: 'var(--gray-light)' }}>{documents.length} belge</span>
                        </div>
                        {/* Arama */}
                        <div className="flex items-center gap-2 px-3 py-2 rounded-xl flex-1 max-w-xs"
                            style={{ background: 'var(--surface2)', border: '1px solid var(--border)' }}>
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#64748b" strokeWidth="2">
                                <circle cx="11" cy="11" r="8" /><line x1="21" y1="21" x2="16.65" y2="16.65" />
                            </svg>
                            <input
                                value={search}
                                onChange={(e) => setSearch(e.target.value)}
                                placeholder="Belge ara..."
                                className="flex-1 text-sm bg-transparent outline-none"
                                style={{ color: '#e2e8f0' }}
                            />
                            {search && (
                                <button onClick={() => setSearch('')} style={{ color: '#64748b' }}>
                                    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                        <line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" />
                                    </svg>
                                </button>
                            )}
                        </div>
                    </div>

                    {filtered.length === 0 ? (
                        <div className="text-center py-16">
                            <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="#334155" strokeWidth="1.5" className="mx-auto mb-3">
                                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                                <polyline points="14 2 14 8 20 8" />
                            </svg>
                            <p className="text-sm" style={{ color: 'var(--gray-light)' }}>
                                {search ? 'Arama sonucu bulunamadı' : 'Henüz belge yüklenmedi'}
                            </p>
                        </div>
                    ) : (
                        <div className="divide-y" style={{ borderColor: 'var(--border)' }}>
                            {filtered.map((doc) => {
                                const st = statusLabel(doc.status);
                                return (
                                    <div key={doc.id} className="flex items-center justify-between px-6 py-4 transition-all"
                                        onMouseEnter={(e) => e.currentTarget.style.background = 'var(--surface2)'}
                                        onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}>
                                        <div className="flex items-center gap-4 min-w-0">
                                            <div className="w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0"
                                                style={{ background: 'rgba(59,130,246,0.1)' }}>
                                                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#3b82f6" strokeWidth="2">
                                                    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                                                    <polyline points="14 2 14 8 20 8" />
                                                </svg>
                                            </div>
                                            <div className="min-w-0">
                                                <p className="text-sm font-medium text-white truncate">{doc.fileName}</p>
                                                <p className="text-xs mt-0.5" style={{ color: 'var(--gray-light)' }}>
                                                    {formatSize(doc.fileSizeBytes)} · {doc.chunkCount} chunk
                                                </p>
                                                {doc.errorMessage && (
                                                    <p className="text-xs mt-0.5" style={{ color: '#f87171' }}>{doc.errorMessage}</p>
                                                )}
                                            </div>
                                        </div>
                                        <div className="flex items-center gap-3 ml-4 flex-shrink-0">
                                            <span className="text-xs px-2.5 py-1 rounded-lg font-medium"
                                                style={{ background: st.bg, color: st.color }}>
                                                {st.label}
                                            </span>
                                            {/* Chunk görüntüle */}
                                            {doc.status === 'Ready' && (
                                                <button onClick={() => handleViewChunks(doc)}
                                                    className="p-2 rounded-lg transition-all"
                                                    style={{ color: '#64748b' }}
                                                    title="Chunk içeriğini gör"
                                                    onMouseEnter={(e) => { e.currentTarget.style.color = '#93c5fd'; e.currentTarget.style.background = 'rgba(147,197,253,0.1)'; }}
                                                    onMouseLeave={(e) => { e.currentTarget.style.color = '#64748b'; e.currentTarget.style.background = 'transparent'; }}>
                                                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                                        <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" />
                                                        <circle cx="12" cy="12" r="3" />
                                                    </svg>
                                                </button>
                                            )}
                                            {/* Sil */}
                                            <button onClick={() => handleDelete(doc.id, doc.fileName)}
                                                className="p-2 rounded-lg transition-all"
                                                style={{ color: '#64748b' }}
                                                onMouseEnter={(e) => { e.currentTarget.style.color = '#f87171'; e.currentTarget.style.background = 'rgba(248,113,113,0.1)'; }}
                                                onMouseLeave={(e) => { e.currentTarget.style.color = '#64748b'; e.currentTarget.style.background = 'transparent'; }}>
                                                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                                    <polyline points="3 6 5 6 21 6" />
                                                    <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
                                                </svg>
                                            </button>
                                        </div>
                                    </div>
                                );
                            })}
                        </div>
                    )}
                </div>
            </div>

            {/* Chunk Modal */}
            {showChunksModal && (
                <div className="fixed inset-0 z-50 flex items-center justify-center p-4"
                    style={{ background: 'rgba(0,0,0,0.7)' }}
                    onClick={() => setShowChunksModal(false)}>
                    <div className="w-full max-w-2xl max-h-[80vh] flex flex-col rounded-2xl overflow-hidden"
                        style={{ background: 'var(--surface)', border: '1px solid var(--border)' }}
                        onClick={(e) => e.stopPropagation()}>
                        {/* Modal header */}
                        <div className="px-6 py-4 flex items-center justify-between flex-shrink-0"
                            style={{ borderBottom: '1px solid var(--border)' }}>
                            <div>
                                <h3 className="font-semibold text-white text-sm">{selectedDoc?.fileName}</h3>
                                <p className="text-xs mt-0.5" style={{ color: 'var(--gray-light)' }}>
                                    {chunksLoading ? 'Yükleniyor...' : `${chunks.length} chunk`}
                                </p>
                            </div>
                            <button onClick={() => setShowChunksModal(false)}
                                className="p-2 rounded-lg transition-all"
                                style={{ color: '#64748b' }}
                                onMouseEnter={(e) => e.currentTarget.style.color = '#e2e8f0'}
                                onMouseLeave={(e) => e.currentTarget.style.color = '#64748b'}>
                                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                    <line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" />
                                </svg>
                            </button>
                        </div>
                        {/* Modal içerik */}
                        <div className="overflow-y-auto p-4 space-y-3">
                            {chunksLoading ? (
                                <div className="text-center py-8">
                                    <div className="w-6 h-6 rounded-full border-2 animate-spin mx-auto"
                                        style={{ borderColor: 'var(--accent)', borderTopColor: 'transparent' }} />
                                </div>
                            ) : chunks.length === 0 ? (
                                <p className="text-sm text-center py-8" style={{ color: 'var(--gray-light)' }}>Chunk bulunamadı</p>
                            ) : chunks.map((chunk) => (
                                <div key={chunk.id} className="rounded-xl p-4"
                                    style={{ background: 'var(--surface2)', border: '1px solid var(--border)' }}>
                                    <div className="flex items-center gap-2 mb-2">
                                        <span className="text-xs px-2 py-0.5 rounded-md font-medium"
                                            style={{ background: 'rgba(59,130,246,0.15)', color: '#93c5fd', border: '1px solid rgba(59,130,246,0.2)' }}>
                                            Chunk #{chunk.chunkIndex + 1}
                                        </span>
                                        <span className="text-xs" style={{ color: '#475569' }}>{chunk.content.length} karakter</span>
                                    </div>
                                    <p className="text-xs leading-relaxed whitespace-pre-wrap" style={{ color: '#94a3b8' }}>
                                        {chunk.content}
                                    </p>
                                </div>
                            ))}
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}