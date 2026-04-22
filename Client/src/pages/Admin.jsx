import { useState, useEffect, useRef, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { getDocuments, uploadDocument, deleteDocument, getDocumentChunks, adminGetUsers, adminCreateUser, adminDeleteUser } from '../services/api';
import { useToast } from '../components/shared/Toast';
import { DocumentSkeleton, UserSkeleton } from '../components/shared/Skeleton';
import Modal from '../components/shared/Modal';
import SearchInput from '../components/shared/SearchInput';

// ── Yardımcı ──────────────────────────────────────────────────────────────
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

// ── Ana Bileşen ────────────────────────────────────────────────────────────
export default function Admin() {
    const [tab, setTab] = useState('documents'); // 'documents' | 'users'

    // Documents state
    const [documents, setDocuments] = useState([]);
    const [docsLoading, setDocsLoading] = useState(true);
    const [uploads, setUploads] = useState([]);
    const [docSearch, setDocSearch] = useState('');
    const [dragOver, setDragOver] = useState(false);
    const [selectedDoc, setSelectedDoc] = useState(null);
    const [chunks, setChunks] = useState([]);
    const [chunksLoading, setChunksLoading] = useState(false);
    const [showChunksModal, setShowChunksModal] = useState(false);
    const fileInputRef = useRef();

    // Users state
    const [users, setUsers] = useState([]);
    const [usersLoading, setUsersLoading] = useState(false);
    const [userSearch, setUserSearch] = useState('');
    const [showUserModal, setShowUserModal] = useState(false);
    const [newUser, setNewUser] = useState({ fullName: '', email: '', password: '' });
    const [userFormError, setUserFormError] = useState('');
    const [userFormLoading, setUserFormLoading] = useState(false);

    const navigate = useNavigate();
    const toast = useToast();

    useEffect(() => { fetchDocs(); }, []);
    useEffect(() => { if (tab === 'users' && users.length === 0) fetchUsers(); }, [tab]);

    // ── Belgeler ────────────────────────────────────────────────────────────
    const fetchDocs = useCallback(async (silent = false) => {
        if (!silent) setDocsLoading(true);
        try {
            const res = await getDocuments();
            const docs = res.data.data || [];
            setDocuments(docs);
            // Processing belgeler varsa 3 saniye sonra tekrar kontrol et
            if (docs.some(d => d.status === 'Processing' || d.status === 'Pending')) {
                setTimeout(() => fetchDocs(true), 3000);
            }
        } catch { if (!silent) toast.error('Belgeler yüklenemedi.'); }
        finally { if (!silent) setDocsLoading(false); }
    }, []);

    const processFiles = (files) => {
        const allowed = ['application/pdf',
            'application/msword',                                                          // .doc
            'application/vnd.openxmlformats-officedocument.wordprocessingml.document',    // .docx
            'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',          // .xlsx
            'text/plain', 'text/csv'];
        const valid = Array.from(files).filter(f => allowed.includes(f.type));
        if (valid.length === 0) { toast.error('Desteklenmeyen format. PDF, DOCX, XLSX veya CSV yükleyin.'); return; }
        valid.forEach(uploadFile);
    };

    const uploadFile = async (file) => {
        const uid = Date.now() + Math.random();
        setUploads(prev => [...prev, { id: uid, name: file.name, progress: 0, status: 'uploading' }]);
        try {
            await uploadDocument(file, (p) => {
                setUploads(prev => prev.map(u => u.id === uid ? { ...u, progress: p } : u));
            });
            setUploads(prev => prev.map(u => u.id === uid ? { ...u, progress: 100, status: 'done' } : u));
            toast.success(`"${file.name}" başarıyla yüklendi.`);
            fetchDocs();
            setTimeout(() => setUploads(prev => prev.filter(u => u.id !== uid)), 3000);
        } catch (err) {
            const msg = err.response?.data?.error?.message || err.message;
            setUploads(prev => prev.map(u => u.id === uid ? { ...u, status: 'error', error: msg } : u));
            toast.error(`"${file.name}" yüklenemedi.`);
            setTimeout(() => setUploads(prev => prev.filter(u => u.id !== uid)), 5000);
        }
    };

    const handleDeleteDoc = async (id, name) => {
        if (!confirm(`"${name}" silinecek. Emin misiniz?`)) return;
        try {
            await deleteDocument(id);
            setDocuments(prev => prev.filter(d => d.id !== id));
            toast.success(`"${name}" silindi.`);
            if (selectedDoc?.id === id) setShowChunksModal(false);
        } catch { toast.error('Belge silinemedi.'); }
    };

    const handleViewChunks = async (doc) => {
        setSelectedDoc(doc);
        setShowChunksModal(true);
        setChunksLoading(true);
        setChunks([]);
        try {
            const res = await getDocumentChunks(doc.id);
            setChunks(res.data.data || []);
        } catch { toast.error('Chunk\'lar yüklenemedi.'); }
        finally { setChunksLoading(false); }
    };

    // ── Kullanıcılar ────────────────────────────────────────────────────────
    const fetchUsers = async () => {
        setUsersLoading(true);
        try {
            const res = await adminGetUsers();
            setUsers(res.data.data || []);
        } catch { toast.error('Kullanıcılar yüklenemedi.'); }
        finally { setUsersLoading(false); }
    };

    const handleCreateUser = async (e) => {
        e.preventDefault();
        setUserFormError('');
        setUserFormLoading(true);
        try {
            await adminCreateUser(newUser.fullName, newUser.email, newUser.password);
            toast.success(`"${newUser.fullName}" oluşturuldu.`);
            setShowUserModal(false);
            setNewUser({ fullName: '', email: '', password: '' });
            fetchUsers();
        } catch (err) {
            const msg = err.response?.data?.error?.message;
            const errors = err.response?.data?.error?.errors;
            const errorText = errors?.join(' ') || msg || 'Kullanıcı oluşturulamadı.';
            setUserFormError(errorText);
            toast.error(errorText);
        } finally { setUserFormLoading(false); }
    };

    const handleDeleteUser = async (id, name) => {
        if (!confirm(`"${name}" silinecek. Emin misiniz?`)) return;
        try {
            await adminDeleteUser(id);
            setUsers(prev => prev.filter(u => u.id !== id));
            toast.success(`"${name}" silindi.`);
        } catch (err) {
            const msg = err.response?.data?.error?.message || 'Kullanıcı silinemedi.';
            toast.error(msg);
        }
    };

    // ── Filtered lists ───────────────────────────────────────────────────────
    const filteredDocs = documents.filter(d => d.fileName.toLowerCase().includes(docSearch.toLowerCase()));
    const filteredUsers = users.filter(u =>
        u.fullName.toLowerCase().includes(userSearch.toLowerCase()) ||
        u.email.toLowerCase().includes(userSearch.toLowerCase())
    );

    // ── Render ───────────────────────────────────────────────────────────────
    return (
        <div className="min-h-screen" style={{ background: 'var(--navy)' }}>
            {/* Header */}
            <div className="px-8 py-5 flex items-center justify-between"
                style={{ background: 'var(--surface)', borderBottom: '1px solid var(--border)' }}>
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
                {/* Tab seçici */}
                <div className="flex items-center gap-1 p-1 rounded-xl" style={{ background: 'var(--surface2)', border: '1px solid var(--border)' }}>
                    {[
                        { key: 'documents', label: 'Belgeler', icon: <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /> },
                        { key: 'users', label: 'Kullanıcılar', icon: <><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M23 21v-2a4 4 0 0 0-3-3.87" /><path d="M16 3.13a4 4 0 0 1 0 7.75" /></> },
                    ].map(t => (
                        <button key={t.key} onClick={() => setTab(t.key)}
                            className="flex items-center gap-2 px-4 py-2 rounded-lg text-sm font-medium transition-all"
                            style={{
                                background: tab === t.key ? 'var(--accent)' : 'transparent',
                                color: tab === t.key ? 'white' : '#94a3b8',
                            }}>
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                {t.icon}
                            </svg>
                            {t.label}
                            {t.key === 'documents' && <span className="text-xs px-1.5 py-0.5 rounded-md" style={{ background: tab === 'documents' ? 'rgba(255,255,255,0.2)' : 'var(--border)', color: tab === 'documents' ? 'white' : '#64748b' }}>{documents.length}</span>}
                            {t.key === 'users' && <span className="text-xs px-1.5 py-0.5 rounded-md" style={{ background: tab === 'users' ? 'rgba(255,255,255,0.2)' : 'var(--border)', color: tab === 'users' ? 'white' : '#64748b' }}>{users.length}</span>}
                        </button>
                    ))}
                </div>
            </div>

            <div className="max-w-5xl mx-auto px-8 py-8">

                {/* ── BELGELER SEKMESI ── */}
                {tab === 'documents' && (
                    <>
                        {/* Upload */}
                        <div className="mb-8 rounded-2xl p-6" style={{ background: 'var(--surface)', border: '1px solid var(--border)' }}>
                            <h2 className="text-base font-semibold text-white mb-1">Belge Yükle</h2>
                            <p className="text-sm mb-5" style={{ color: 'var(--gray-light)' }}>
                                Birden fazla dosya seçebilir veya sürükleyip bırakabilirsiniz.
                            </p>
                            <div
                                className="rounded-xl p-8 text-center cursor-pointer transition-all"
                                style={{
                                    border: `2px dashed ${dragOver ? 'var(--accent)' : 'var(--border)'}`,
                                    background: dragOver ? 'rgba(59,130,246,0.05)' : 'var(--surface2)',
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
                                <p className="text-xs" style={{ color: 'var(--gray-light)' }}>PDF, DOC, DOCX, XLSX, CSV · Maks. 50 MB · Çoklu seçim desteklenir</p>
                            </div>
                            <input ref={fileInputRef} type="file" accept=".pdf,.doc,.docx,.xlsx,.csv" className="hidden" multiple onChange={(e) => { processFiles(e.target.files); fileInputRef.current.value = ''; }} />

                            {/* Progress listesi */}
                            {uploads.length > 0 && (
                                <div className="mt-4 space-y-2">
                                    {uploads.map((u) => (
                                        <div key={u.id} className="px-4 py-3 rounded-xl" style={{
                                            background: u.status === 'error' ? 'rgba(239,68,68,0.08)' : 'rgba(59,130,246,0.08)',
                                            border: `1px solid ${u.status === 'error' ? 'rgba(239,68,68,0.2)' : 'rgba(59,130,246,0.2)'}`,
                                        }}>
                                            <div className="flex items-center justify-between mb-1.5">
                                                <span className="text-xs font-medium truncate max-w-xs"
                                                    style={{ color: u.status === 'error' ? '#fca5a5' : '#93c5fd' }}>{u.name}</span>
                                                <span className="text-xs ml-2 flex-shrink-0"
                                                    style={{ color: u.status === 'error' ? '#fca5a5' : '#93c5fd' }}>
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
                        </div>

                        {/* Belge listesi */}
                        <div className="rounded-2xl overflow-hidden" style={{ background: 'var(--surface)', border: '1px solid var(--border)' }}>
                            <div className="px-6 py-4 flex items-center justify-between gap-4"
                                style={{ borderBottom: '1px solid var(--border)' }}>
                                <div className="flex items-center gap-3">
                                    <h2 className="text-base font-semibold text-white">Yüklü Belgeler</h2>
                                    <span className="text-sm" style={{ color: 'var(--gray-light)' }}>{documents.length} belge</span>
                                </div>
                                <div className="flex-1 max-w-xs">
                                    <SearchInput value={docSearch} onChange={setDocSearch} placeholder="Belge ara..." />
                                </div>
                            </div>

                            {docsLoading ? <DocumentSkeleton /> : filteredDocs.length === 0 ? (
                                <div className="text-center py-16">
                                    <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="#334155" strokeWidth="1.5" className="mx-auto mb-3">
                                        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" />
                                    </svg>
                                    <p className="text-sm" style={{ color: 'var(--gray-light)' }}>
                                        {docSearch ? 'Arama sonucu bulunamadı' : 'Henüz belge yüklenmedi'}
                                    </p>
                                </div>
                            ) : (
                                <div className="divide-y" style={{ borderColor: 'var(--border)' }}>
                                    {filteredDocs.map((doc) => {
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
                                                            {formatSize(doc.fileSizeBytes)} · {doc.chunkCount} chunk · {formatDate(doc.createdAt)}
                                                        </p>
                                                        {doc.errorMessage && <p className="text-xs mt-0.5" style={{ color: '#f87171' }}>{doc.errorMessage}</p>}
                                                    </div>
                                                </div>
                                                <div className="flex items-center gap-3 ml-4 flex-shrink-0">
                                                    <span className="text-xs px-2.5 py-1 rounded-lg font-medium" style={{ background: st.bg, color: st.color }}>{st.label}</span>
                                                    {doc.status === 'Ready' && (
                                                        <button onClick={() => handleViewChunks(doc)}
                                                            className="p-2 rounded-lg transition-all" style={{ color: '#64748b', background: 'none', border: 'none', cursor: 'pointer' }} title="Chunk içeriğini gör"
                                                            onMouseEnter={(e) => { e.currentTarget.style.color = '#93c5fd'; e.currentTarget.style.background = 'rgba(147,197,253,0.1)'; }}
                                                            onMouseLeave={(e) => { e.currentTarget.style.color = '#64748b'; e.currentTarget.style.background = 'transparent'; }}>
                                                            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                                                <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" /><circle cx="12" cy="12" r="3" />
                                                            </svg>
                                                        </button>
                                                    )}
                                                    <button onClick={() => handleDeleteDoc(doc.id, doc.fileName)}
                                                        className="p-2 rounded-lg transition-all" style={{ color: '#64748b', background: 'none', border: 'none', cursor: 'pointer' }}
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
                            )}
                        </div>
                    </>
                )}

                {/* ── KULLANICILAR SEKMESI ── */}
                {tab === 'users' && (
                    <div className="rounded-2xl overflow-hidden" style={{ background: 'var(--surface)', border: '1px solid var(--border)' }}>
                        <div className="px-6 py-4 flex items-center justify-between gap-4"
                            style={{ borderBottom: '1px solid var(--border)' }}>
                            <div className="flex items-center gap-3">
                                <h2 className="text-base font-semibold text-white">Kullanıcılar</h2>
                                <span className="text-sm" style={{ color: 'var(--gray-light)' }}>{users.length} kullanıcı</span>
                            </div>
                            <div className="flex items-center gap-3">
                                <SearchInput value={userSearch} onChange={setUserSearch} placeholder="Kullanıcı ara..." />
                                <button onClick={() => { setShowUserModal(true); setUserFormError(''); setNewUser({ fullName: '', email: '', password: '' }); }}
                                    className="flex items-center gap-2 px-4 py-2 rounded-xl text-sm font-medium text-white transition-all"
                                    style={{ background: 'var(--accent)' }}
                                    onMouseEnter={(e) => e.currentTarget.style.opacity = '0.85'}
                                    onMouseLeave={(e) => e.currentTarget.style.opacity = '1'}>
                                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                                        <line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" />
                                    </svg>
                                    Kullanıcı Ekle
                                </button>
                            </div>
                        </div>

                        {usersLoading ? <UserSkeleton /> : filteredUsers.length === 0 ? (
                            <div className="text-center py-16">
                                <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="#334155" strokeWidth="1.5" className="mx-auto mb-3">
                                    <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" />
                                </svg>
                                <p className="text-sm" style={{ color: 'var(--gray-light)' }}>
                                    {userSearch ? 'Arama sonucu bulunamadı' : 'Kullanıcı bulunamadı'}
                                </p>
                            </div>
                        ) : (
                            <div className="divide-y" style={{ borderColor: 'var(--border)' }}>
                                {filteredUsers.map((u) => {
                                    const isAdmin = u.roles?.includes('Admin');
                                    return (
                                        <div key={u.id} className="flex items-center justify-between px-6 py-4 transition-all"
                                            onMouseEnter={(e) => e.currentTarget.style.background = 'var(--surface2)'}
                                            onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}>
                                            <div className="flex items-center gap-4 min-w-0">
                                                <div className="w-9 h-9 rounded-full flex items-center justify-center flex-shrink-0 font-semibold text-sm"
                                                    style={{ background: isAdmin ? 'rgba(59,130,246,0.2)' : 'rgba(100,116,139,0.2)', color: isAdmin ? '#93c5fd' : '#94a3b8' }}>
                                                    {u.fullName?.charAt(0)?.toUpperCase() || '?'}
                                                </div>
                                                <div className="min-w-0">
                                                    <div className="flex items-center gap-2">
                                                        <p className="text-sm font-medium text-white truncate">{u.fullName}</p>
                                                        {isAdmin && (
                                                            <span className="text-xs px-2 py-0.5 rounded-md font-medium flex-shrink-0"
                                                                style={{ background: 'rgba(59,130,246,0.15)', color: '#93c5fd', border: '1px solid rgba(59,130,246,0.2)' }}>
                                                                Admin
                                                            </span>
                                                        )}
                                                    </div>
                                                    <p className="text-xs mt-0.5" style={{ color: 'var(--gray-light)' }}>
                                                        {u.email} · {formatDate(u.createdAt)}
                                                    </p>
                                                </div>
                                            </div>
                                            {!isAdmin && (
                                                <button onClick={() => handleDeleteUser(u.id, u.fullName)}
                                                    className="p-2 rounded-lg transition-all flex-shrink-0"
                                                    style={{ color: '#64748b', background: 'none', border: 'none', cursor: 'pointer' }}
                                                    onMouseEnter={(e) => { e.currentTarget.style.color = '#f87171'; e.currentTarget.style.background = 'rgba(248,113,113,0.1)'; }}
                                                    onMouseLeave={(e) => { e.currentTarget.style.color = '#64748b'; e.currentTarget.style.background = 'transparent'; }}>
                                                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                                        <polyline points="3 6 5 6 21 6" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
                                                    </svg>
                                                </button>
                                            )}
                                        </div>
                                    );
                                })}
                            </div>
                        )}
                    </div>
                )}
            </div>

            {/* ── CHUNK MODAL ── */}
            {showChunksModal && (
                <Modal
                    title={selectedDoc?.fileName}
                    subtitle={chunksLoading ? 'Yükleniyor...' : `${chunks.length} chunk`}
                    onClose={() => setShowChunksModal(false)}
                    maxWidth="max-w-2xl"
                >
                    <div className="p-4 space-y-3">
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
                </Modal>
            )}

            {/* ── KULLANICI EKLEME MODAL ── */}
            {showUserModal && (
                <Modal title="Yeni Kullanıcı" onClose={() => setShowUserModal(false)}>
                    <form onSubmit={handleCreateUser} className="p-6 space-y-4">
                        {userFormError && (
                            <div className="p-3 rounded-lg text-sm"
                                style={{ background: 'rgba(239,68,68,0.1)', border: '1px solid rgba(239,68,68,0.3)', color: '#fca5a5' }}>
                                {userFormError}
                            </div>
                        )}
                        {[
                            { label: 'Ad Soyad', key: 'fullName', type: 'text', placeholder: 'Ad Soyad' },
                            { label: 'E-posta', key: 'email', type: 'email', placeholder: 'ornek@sirket.com' },
                            { label: 'Şifre', key: 'password', type: 'password', placeholder: 'En az 8 karakter' },
                        ].map(({ label, key, type, placeholder }) => (
                            <div key={key}>
                                <label className="block text-sm font-medium mb-2" style={{ color: '#94a3b8' }}>{label}</label>
                                <input type={type} value={newUser[key]} required placeholder={placeholder}
                                    onChange={(e) => setNewUser(prev => ({ ...prev, [key]: e.target.value }))}
                                    className="w-full px-4 py-3 rounded-xl text-white placeholder-slate-500 outline-none transition-all"
                                    style={{ background: 'var(--surface2)', border: '1px solid var(--border)', fontSize: '0.9rem' }}
                                    onFocus={(e) => e.target.style.borderColor = 'var(--accent)'}
                                    onBlur={(e) => e.target.style.borderColor = 'var(--border)'}
                                />
                            </div>
                        ))}
                        <p className="text-xs" style={{ color: '#475569' }}>
                            Şifre: büyük/küçük harf, rakam ve özel karakter içermeli
                        </p>
                        <div className="flex gap-3 pt-2">
                            <button type="button" onClick={() => setShowUserModal(false)}
                                className="flex-1 py-3 rounded-xl text-sm font-medium transition-all"
                                style={{ background: 'var(--surface2)', color: '#94a3b8', border: '1px solid var(--border)' }}
                                onMouseEnter={(e) => e.currentTarget.style.borderColor = 'var(--accent)'}
                                onMouseLeave={(e) => e.currentTarget.style.borderColor = 'var(--border)'}>
                                İptal
                            </button>
                            <button type="submit" disabled={userFormLoading}
                                className="flex-1 py-3 rounded-xl text-sm font-semibold text-white transition-all"
                                style={{ background: userFormLoading ? 'var(--navy-light)' : 'var(--accent)', cursor: userFormLoading ? 'not-allowed' : 'pointer' }}>
                                {userFormLoading ? 'Oluşturuluyor...' : 'Oluştur'}
                            </button>
                        </div>
                    </form>
                </Modal>
            )}
        </div>
    );
}