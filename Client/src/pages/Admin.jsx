import { useState, useEffect, useRef, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { getDocuments, uploadDocument, deleteDocument, getDocumentChunks, adminGetUsers, adminCreateUser, adminDeleteUser } from '../services/api';
import { useToast } from '../components/shared/Toast';
import Modal from '../components/shared/Modal';
import DocumentUpload from '../components/admin/DocumentUpload';
import DocumentList from '../components/admin/DocumentList';
import UserList from '../components/admin/UserList';
import UserModal from '../components/admin/UserModal';

export default function Admin() {
    const [tab, setTab] = useState('documents');

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

    const [users, setUsers] = useState([]);
    const [usersLoading, setUsersLoading] = useState(true);
    const [userSearch, setUserSearch] = useState('');
    const [showUserModal, setShowUserModal] = useState(false);
    const [newUser, setNewUser] = useState({ fullName: '', email: '', password: '' });
    const [userFormError, setUserFormError] = useState('');
    const [userFormLoading, setUserFormLoading] = useState(false);

    const navigate = useNavigate();
    const toast = useToast();

    useEffect(() => { fetchDocs(); fetchUsers(); }, []);

    const fetchDocs = useCallback(async (silent = false) => {
        if (!silent) setDocsLoading(true);
        try {
            const res = await getDocuments();
            const docs = res.data.data || [];
            setDocuments(docs);
            if (docs.some(d => d.status === 'Processing' || d.status === 'Pending'))
                setTimeout(() => fetchDocs(true), 3000);
        } catch { if (!silent) toast.error('Belgeler yüklenemedi.'); }
        finally { if (!silent) setDocsLoading(false); }
    }, []);

    const fetchUsers = useCallback(async () => {
        setUsersLoading(true);
        try {
            const res = await adminGetUsers();
            setUsers(res.data.data || []);
        } catch { }
        finally { setUsersLoading(false); }
    }, []);

    const processFiles = (files) => {
        const allowed = ['application/pdf', 'application/msword',
            'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
            'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
            'text/plain', 'text/csv'];
        const valid = Array.from(files).filter(f => allowed.includes(f.type));
        if (valid.length === 0) { toast.error('Desteklenmeyen format.'); return; }
        valid.forEach(uploadFile);
    };

    const uploadFile = async (file) => {
        const uid = Date.now() + Math.random();
        setUploads(prev => [...prev, { id: uid, name: file.name, progress: 0, status: 'uploading' }]);
        try {
            await uploadDocument(file, (p) => setUploads(prev => prev.map(u => u.id === uid ? { ...u, progress: p } : u)));
            setUploads(prev => prev.map(u => u.id === uid ? { ...u, progress: 100, status: 'done' } : u));
            toast.success(`"${file.name}" yüklendi.`);
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
        } catch { toast.error("Chunk'lar yüklenemedi."); }
        finally { setChunksLoading(false); }
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
            setUserFormError(errors?.join(' ') || msg || 'Kullanıcı oluşturulamadı.');
        } finally { setUserFormLoading(false); }
    };

    const handleDeleteUser = async (id, name) => {
        if (!confirm(`"${name}" silinecek. Emin misiniz?`)) return;
        try {
            await adminDeleteUser(id);
            setUsers(prev => prev.filter(u => u.id !== id));
            toast.success(`"${name}" silindi.`);
        } catch (err) {
            toast.error(err.response?.data?.error?.message || 'Kullanıcı silinemedi.');
        }
    };

    return (
        <div style={{ minHeight: '100vh', background: 'var(--navy)' }}>
            {/* Header */}
            <div className="admin-header" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '16px 24px', background: 'var(--surface)', borderBottom: '1px solid var(--border)', gap: '12px', flexWrap: 'wrap' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                    <button onClick={() => navigate('/chat')}
                        style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '14px', color: '#94a3b8', background: 'none', border: 'none', cursor: 'pointer', padding: '6px 10px', borderRadius: '8px' }}
                        onMouseEnter={(e) => e.currentTarget.style.background = 'var(--surface2)'}
                        onMouseLeave={(e) => e.currentTarget.style.background = 'none'}>
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                            <line x1="19" y1="12" x2="5" y2="12" /><polyline points="12 19 5 12 12 5" />
                        </svg>
                        Geri
                    </button>
                    <div style={{ width: '1px', height: '20px', background: 'var(--border)' }} />
                    <h1 style={{ fontSize: '16px', fontWeight: 700, color: 'white', margin: 0 }}>Admin Panel</h1>
                </div>

                <div className="admin-tabs" style={{ display: 'flex', gap: '4px', padding: '4px', background: 'var(--surface2)', border: '1px solid var(--border)', borderRadius: '12px' }}>
                    {[
                        { key: 'documents', label: 'Belgeler', count: documents.length, icon: <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /> },
                        { key: 'users', label: 'Kullanıcılar', count: users.length, icon: <><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /></> },
                    ].map(t => (
                        <button key={t.key} onClick={() => setTab(t.key)} className="admin-tab-btn"
                            style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '8px 14px', borderRadius: '8px', fontSize: '14px', fontWeight: 500, background: tab === t.key ? 'var(--accent)' : 'transparent', color: tab === t.key ? 'white' : '#94a3b8', border: 'none', cursor: 'pointer', whiteSpace: 'nowrap' }}>
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">{t.icon}</svg>
                            <span className="admin-tab-label">{t.label}</span>
                            <span style={{ fontSize: '12px', padding: '1px 7px', borderRadius: '6px', background: tab === t.key ? 'rgba(255,255,255,0.2)' : 'var(--border)', color: tab === t.key ? 'white' : '#64748b', minWidth: '20px', textAlign: 'center' }}>{t.count}</span>
                        </button>
                    ))}
                </div>
            </div>

            <div className="admin-content" style={{ maxWidth: '900px', margin: '0 auto', padding: '32px 24px' }}>
                {tab === 'documents' && (
                    <>
                        <DocumentUpload
                            uploads={uploads}
                            dragOver={dragOver}
                            onDragOver={(e) => { e.preventDefault(); setDragOver(true); }}
                            onDragLeave={() => setDragOver(false)}
                            onDrop={(e) => { e.preventDefault(); setDragOver(false); processFiles(e.dataTransfer.files); }}
                            onClick={() => fileInputRef.current.click()}
                            fileInputRef={fileInputRef}
                            onFileChange={(e) => { processFiles(e.target.files); fileInputRef.current.value = ''; }}
                        />
                        <DocumentList
                            documents={documents}
                            loading={docsLoading}
                            search={docSearch}
                            onSearchChange={setDocSearch}
                            onViewChunks={handleViewChunks}
                            onDelete={handleDeleteDoc}
                        />
                    </>
                )}

                {tab === 'users' && (
                    <UserList
                        users={users}
                        loading={usersLoading}
                        search={userSearch}
                        onSearchChange={setUserSearch}
                        onAdd={() => { setShowUserModal(true); setUserFormError(''); setNewUser({ fullName: '', email: '', password: '' }); }}
                        onDelete={handleDeleteUser}
                    />
                )}
            </div>

            {/* Chunk Modal */}
            {showChunksModal && (
                <Modal title={selectedDoc?.fileName} subtitle={chunksLoading ? 'Yükleniyor...' : `${chunks.length} chunk`} onClose={() => setShowChunksModal(false)} maxWidth="max-w-2xl">
                    <div style={{ padding: '16px', display: 'flex', flexDirection: 'column', gap: '12px' }}>
                        {chunksLoading ? (
                            <div style={{ textAlign: 'center', padding: '32px' }}>
                                <div style={{ width: '24px', height: '24px', borderRadius: '50%', border: '2px solid var(--accent)', borderTopColor: 'transparent', animation: 'spin 0.8s linear infinite', margin: '0 auto' }} />
                            </div>
                        ) : chunks.map((chunk) => (
                            <div key={chunk.id} style={{ borderRadius: '12px', padding: '16px', background: 'var(--surface2)', border: '1px solid var(--border)' }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px' }}>
                                    <span style={{ fontSize: '11px', padding: '2px 8px', borderRadius: '6px', fontWeight: 500, background: 'rgba(59,130,246,0.15)', color: '#93c5fd', border: '1px solid rgba(59,130,246,0.2)' }}>Chunk #{chunk.chunkIndex + 1}</span>
                                    <span style={{ fontSize: '12px', color: '#475569' }}>{chunk.content.length} karakter</span>
                                </div>
                                <p style={{ fontSize: '12px', lineHeight: 1.6, whiteSpace: 'pre-wrap', color: '#94a3b8', margin: 0 }}>{chunk.content}</p>
                            </div>
                        ))}
                    </div>
                </Modal>
            )}

            {/* User Modal */}
            {showUserModal && (
                <UserModal
                    onClose={() => setShowUserModal(false)}
                    onSubmit={handleCreateUser}
                    user={newUser}
                    onChange={(key, val) => setNewUser(prev => ({ ...prev, [key]: val }))}
                    error={userFormError}
                    loading={userFormLoading}
                />
            )}
        </div>
    );
}