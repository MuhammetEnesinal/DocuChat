import { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { API_BASE } from '../services/api';
import Modal from '../components/shared/Modal';
import DocumentUpload from '../components/admin/DocumentUpload';
import DocumentList from '../components/admin/DocumentList';
import UserList from '../components/admin/UserList';
import UserModal from '../components/admin/UserModal';
import ThemeToggle from '../components/shared/ThemeToggle';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import { useToast } from '../components/shared/Toast';
import { useDocuments } from '../hooks/useDocuments';
import { useUsers } from '../hooks/useUsers';

export default function Admin() {
    const [tab, setTab] = useState('documents');
    const [confirmDoc, setConfirmDoc] = useState(null);
    const [confirmUser, setConfirmUser] = useState(null);
    const [deletingDocId, setDeletingDocId] = useState(null);
    const [deletingUserId, setDeletingUserId] = useState(null);

    const fileInputRef = useRef();
    const navigate = useNavigate();
    const toast = useToast();

    const {
        documents, setDocuments,
        docsLoading,
        uploads,
        docSearch, setDocSearch,
        dragOver, setDragOver,
        selectedDoc,
        chunks,
        chunksLoading,
        showChunksModal, setShowChunksModal,
        fetchDocs,
        deleteDoc,
        handleViewChunks,
        processFiles,
    } = useDocuments();

    const {
        users,
        usersLoading,
        userSearch, setUserSearch,
        showUserModal,
        editingUser,
        userForm,
        setUserForm,
        userFormError,
        userFormLoading,
        fetchUsers,
        openAddModal,
        openEditModal,
        closeUserModal,
        handleSubmitUser,
        deleteUser,
    } = useUsers();

    useEffect(() => { fetchDocs(); fetchUsers(); }, []);

    const handleDeleteDoc = (id, name) => {
        setConfirmDoc({ id, name });
    };

    const confirmDeleteDoc = async () => {
        const { id, name } = confirmDoc;
        setConfirmDoc(null);
        setDeletingDocId(id);
        try {
            await deleteDoc(id);
            toast.success(`"${name}" silindi.`);
        } catch {
            toast.error('Belge silinemedi.');
        } finally {
            setDeletingDocId(null);
        }
    };

    const handleDeleteUser = (id, name) => {
        setConfirmUser({ id, name });
    };

    const confirmDeleteUser = async () => {
        const { id, name } = confirmUser;
        setConfirmUser(null);
        setDeletingUserId(id);
        try {
            await deleteUser(id);
            toast.success(`"${name}" silindi.`);
        } catch (err) {
            toast.error(err?.response?.data?.error?.message || 'Kullanıcı silinemedi.');
        } finally {
            setDeletingUserId(null);
        }
    };

    return (
        <div style={{ minHeight: '100vh', background: 'var(--navy)' }}>
            <div className="admin-header" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '16px 24px', background: 'var(--surface)', borderBottom: '1px solid var(--border)', gap: '12px', flexWrap: 'wrap' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                    <button onClick={() => navigate('/chat')}
                        style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '14px', color: 'var(--text-muted)', background: 'none', border: 'none', cursor: 'pointer', padding: '6px 10px', borderRadius: '8px' }}
                        onMouseEnter={(e) => e.currentTarget.style.background = 'var(--surface2)'}
                        onMouseLeave={(e) => e.currentTarget.style.background = 'none'}>
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                            <line x1="19" y1="12" x2="5" y2="12" /><polyline points="12 19 5 12 12 5" />
                        </svg>
                        Geri
                    </button>
                    <div style={{ width: '1px', height: '20px', background: 'var(--border)' }} />
                    <h1 style={{ fontSize: '16px', fontWeight: 700, color: 'var(--text-primary)', margin: 0 }}>Admin Panel</h1>
                    <ThemeToggle />
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
                            deletingDocId={deletingDocId}
                            onReprocessStart={(id) =>
                                setDocuments(prev => prev.map(d =>
                                    d.id === id ? { ...d, status: 'Processing' } : d
                                ))
                            }
                            onReprocess={() => fetchDocs(true)}
                        />
                    </>
                )}

                {tab === 'users' && (
                    <UserList
                        users={users}
                        loading={usersLoading}
                        search={userSearch}
                        onSearchChange={setUserSearch}
                        onAdd={openAddModal}
                        onEdit={openEditModal}
                        onDelete={handleDeleteUser}
                        deletingUserId={deletingUserId}
                    />
                )}
            </div>

            {showChunksModal && (
                <Modal title={selectedDoc?.fileName} subtitle={chunksLoading ? 'Yükleniyor...' : `${chunks.length} chunk`} onClose={() => setShowChunksModal(false)} maxWidth="max-w-2xl">
                    <div style={{ padding: '16px', display: 'flex', flexDirection: 'column', gap: '12px' }}>
                        {chunksLoading ? (
                            <div style={{ textAlign: 'center', padding: '32px' }}>
                                <div style={{ width: '24px', height: '24px', borderRadius: '50%', border: '2px solid var(--accent)', borderTopColor: 'transparent', animation: 'spin 0.8s linear infinite', margin: '0 auto' }} />
                            </div>
                        ) : chunks.map((chunk) => {
                            let chunkImages = [];
                            if (chunk.imagePath) {
                                try { chunkImages = JSON.parse(chunk.imagePath); } catch { }
                            }
                            return (
                                <div key={chunk.id} style={{ borderRadius: '12px', padding: '16px', background: 'var(--surface2)', border: '1px solid var(--border)' }}>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px' }}>
                                        <span style={{ fontSize: '11px', padding: '2px 8px', borderRadius: '6px', fontWeight: 500, background: 'rgba(59,130,246,0.15)', color: '#93c5fd', border: '1px solid rgba(59,130,246,0.2)' }}>Chunk #{chunk.chunkIndex + 1}</span>
                                        <span style={{ fontSize: '12px', color: '#475569' }}>{chunk.content.length} karakter</span>
                                        {chunkImages.length > 0 && (
                                            <span style={{ fontSize: '11px', padding: '2px 8px', borderRadius: '6px', fontWeight: 500, background: 'rgba(34,197,94,0.15)', color: '#86efac', border: '1px solid rgba(34,197,94,0.2)' }}>{chunkImages.length} görsel</span>
                                        )}
                                    </div>
                                    <p style={{ fontSize: '12px', lineHeight: 1.6, whiteSpace: 'pre-wrap', color: 'var(--text-muted)', margin: 0 }}>{chunk.content}</p>
                                    {chunkImages.length > 0 && (
                                        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px', marginTop: '12px' }}>
                                            {chunkImages.map((imgPath, i) => (
                                                <img
                                                    key={i}
                                                    src={`${API_BASE}/uploads/${imgPath}`}
                                                    alt={`Görsel ${i + 1}`}
                                                    style={{ maxWidth: '120px', maxHeight: '90px', objectFit: 'contain', borderRadius: '6px', border: '1px solid var(--border)', cursor: 'pointer' }}
                                                    onClick={() => window.open(`${API_BASE}/uploads/${imgPath}`, '_blank')}
                                                    onError={e => { e.currentTarget.style.display = 'none'; }}
                                                />
                                            ))}
                                        </div>
                                    )}
                                </div>
                            );
                        })}
                    </div>
                </Modal>
            )}

            {showUserModal && (
                <UserModal
                    onClose={closeUserModal}
                    onSubmit={handleSubmitUser}
                    user={userForm}
                    onChange={setUserForm}
                    error={userFormError}
                    loading={userFormLoading}
                    isEdit={!!editingUser}
                />
            )}

            {confirmDoc && (
                <ConfirmDialog
                    title="Belgeyi Sil"
                    message={`"${confirmDoc.name}" kalıcı olarak silinecek. Emin misiniz?`}
                    confirmLabel="Sil"
                    onConfirm={confirmDeleteDoc}
                    onCancel={() => setConfirmDoc(null)}
                />
            )}

            {confirmUser && (
                <ConfirmDialog
                    title="Kullanıcıyı Sil"
                    message={`"${confirmUser.name}" kalıcı olarak silinecek. Emin misiniz?`}
                    confirmLabel="Sil"
                    onConfirm={confirmDeleteUser}
                    onCancel={() => setConfirmUser(null)}
                />
            )}
        </div>
    );
}
