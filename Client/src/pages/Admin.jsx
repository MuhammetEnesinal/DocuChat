import { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { getDocuments, uploadDocument, deleteDocument } from '../services/api';

export default function Admin() {
    const [documents, setDocuments] = useState([]);
    const [uploading, setUploading] = useState(false);
    const [uploadProgress, setUploadProgress] = useState('');
    const [error, setError] = useState('');
    const fileInputRef = useRef();
    const navigate = useNavigate();

    useEffect(() => { fetchDocs(); }, []);

    const fetchDocs = async () => {
        try {
            const res = await getDocuments();
            setDocuments(res.data.data || []);
        } catch { }
    };

    const handleUpload = async (e) => {
        const file = e.target.files[0];
        if (!file) return;
        setUploading(true);
        setError('');
        setUploadProgress(`"${file.name}" yükleniyor...`);
        try {
            await uploadDocument(file);
            setUploadProgress(`"${file.name}" başarıyla yüklendi.`);
            fetchDocs();
        } catch (err) {
            setError('Yükleme başarısız: ' + (err.response?.data?.error?.message || err.message));
        } finally {
            setUploading(false);
            fileInputRef.current.value = '';
        }
    };

    const handleDelete = async (id, name) => {
        if (!confirm(`"${name}" silinecek. Emin misiniz?`)) return;
        try {
            await deleteDocument(id);
            setDocuments((prev) => prev.filter((d) => d.id !== id));
        } catch { }
    };

    const statusLabel = (status) => {
        const map = { Ready: { label: 'Hazır', color: '#4ade80', bg: 'rgba(74,222,128,0.1)' }, Processing: { label: 'İşleniyor', color: '#fbbf24', bg: 'rgba(251,191,36,0.1)' }, Failed: { label: 'Hata', color: '#f87171', bg: 'rgba(248,113,113,0.1)' }, Pending: { label: 'Bekliyor', color: '#94a3b8', bg: 'rgba(148,163,184,0.1)' } };
        return map[status] || map.Pending;
    };

    const formatSize = (bytes) => {
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
        return (bytes / 1048576).toFixed(1) + ' MB';
    };

    return (
        <div className="min-h-screen" style={{ background: 'var(--navy)' }}>
            {/* Header */}
            <div className="px-8 py-5 flex items-center justify-between" style={{ background: 'var(--surface)', borderBottom: '1px solid var(--border)' }}>
                <div className="flex items-center gap-4">
                    <button
                        onClick={() => navigate('/chat')}
                        className="flex items-center gap-2 text-sm transition-all px-3 py-2 rounded-lg"
                        style={{ color: '#94a3b8' }}
                        onMouseEnter={(e) => e.currentTarget.style.background = 'var(--surface2)'}
                        onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}
                    >
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
                        PDF, DOCX, XLSX veya CSV formatındaki belgelerinizi yükleyin.
                    </p>

                    <div
                        className="rounded-xl p-8 text-center cursor-pointer transition-all"
                        style={{ border: '2px dashed var(--border)', background: 'var(--surface2)' }}
                        onDragOver={(e) => { e.preventDefault(); e.currentTarget.style.borderColor = 'var(--accent)'; }}
                        onDragLeave={(e) => e.currentTarget.style.borderColor = 'var(--border)'}
                        onDrop={(e) => { e.preventDefault(); e.currentTarget.style.borderColor = 'var(--border)'; const file = e.dataTransfer.files[0]; if (file) { const dt = new DataTransfer(); dt.items.add(file); fileInputRef.current.files = dt.files; handleUpload({ target: { files: [file] } }); } }}
                        onClick={() => fileInputRef.current.click()}
                    >
                        <div className="w-12 h-12 rounded-xl flex items-center justify-center mx-auto mb-3" style={{ background: 'rgba(59,130,246,0.15)' }}>
                            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#3b82f6" strokeWidth="2">
                                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                                <polyline points="17 8 12 3 7 8" /><line x1="12" y1="3" x2="12" y2="15" />
                            </svg>
                        </div>
                        <p className="text-sm font-medium text-white mb-1">Dosya seçin veya buraya sürükleyin</p>
                        <p className="text-xs" style={{ color: 'var(--gray-light)' }}>PDF, DOCX, XLSX, CSV · Maks. 50 MB</p>
                    </div>

                    <input ref={fileInputRef} type="file" accept=".pdf,.docx,.xlsx,.csv" className="hidden" onChange={handleUpload} />

                    {uploading && (
                        <div className="mt-4 flex items-center gap-3 px-4 py-3 rounded-xl" style={{ background: 'rgba(59,130,246,0.1)', border: '1px solid rgba(59,130,246,0.2)' }}>
                            <div className="w-4 h-4 rounded-full border-2 border-t-transparent animate-spin" style={{ borderColor: 'var(--accent)', borderTopColor: 'transparent' }} />
                            <span className="text-sm" style={{ color: '#93c5fd' }}>{uploadProgress}</span>
                        </div>
                    )}
                    {error && (
                        <div className="mt-4 px-4 py-3 rounded-xl text-sm" style={{ background: 'rgba(239,68,68,0.1)', border: '1px solid rgba(239,68,68,0.2)', color: '#fca5a5' }}>
                            {error}
                        </div>
                    )}
                    {!uploading && uploadProgress && !error && (
                        <div className="mt-4 px-4 py-3 rounded-xl text-sm" style={{ background: 'rgba(74,222,128,0.1)', border: '1px solid rgba(74,222,128,0.2)', color: '#86efac' }}>
                            {uploadProgress}
                        </div>
                    )}
                </div>

                {/* Belge Listesi */}
                <div className="rounded-2xl overflow-hidden" style={{ background: 'var(--surface)', border: '1px solid var(--border)' }}>
                    <div className="px-6 py-4 flex items-center justify-between" style={{ borderBottom: '1px solid var(--border)' }}>
                        <h2 className="text-base font-semibold text-white">Yüklü Belgeler</h2>
                        <span className="text-sm" style={{ color: 'var(--gray-light)' }}>{documents.length} belge</span>
                    </div>

                    {documents.length === 0 ? (
                        <div className="text-center py-16">
                            <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="#334155" strokeWidth="1.5" className="mx-auto mb-3">
                                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                                <polyline points="14 2 14 8 20 8" />
                            </svg>
                            <p className="text-sm" style={{ color: 'var(--gray-light)' }}>Henüz belge yüklenmedi</p>
                        </div>
                    ) : (
                        <div className="divide-y" style={{ borderColor: 'var(--border)' }}>
                            {documents.map((doc) => {
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
                                        <div className="flex items-center gap-4 ml-4 flex-shrink-0">
                                            <span className="text-xs px-2.5 py-1 rounded-lg font-medium"
                                                style={{ background: st.bg, color: st.color }}>
                                                {st.label}
                                            </span>
                                            <button
                                                onClick={() => handleDelete(doc.id, doc.fileName)}
                                                className="p-2 rounded-lg transition-all"
                                                style={{ color: '#64748b' }}
                                                onMouseEnter={(e) => { e.currentTarget.style.color = '#f87171'; e.currentTarget.style.background = 'rgba(248,113,113,0.1)'; }}
                                                onMouseLeave={(e) => { e.currentTarget.style.color = '#64748b'; e.currentTarget.style.background = 'transparent'; }}
                                            >
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
        </div>
    );
}