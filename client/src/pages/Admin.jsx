import { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { motion, AnimatePresence } from 'framer-motion';
import { API_BASE } from '../services/api';
import Modal from '../components/shared/Modal';
import DocumentUpload from '../components/admin/DocumentUpload';
import DocumentList from '../components/admin/DocumentList';
import UserList from '../components/admin/UserList';
import UserModal from '../components/admin/UserModal';
import BulkImportUsersModal from '../components/admin/BulkImportUsersModal';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import { useToast } from '../components/shared/Toast';
import { showApiError } from '../utils/format';
import { useDocuments } from '../hooks/useDocuments';
import { useUsers } from '../hooks/useUsers';

export default function Admin() {
    const [tab, setTab] = useState('documents');
    const [confirmDoc, setConfirmDoc] = useState(null);
    const [confirmUser, setConfirmUser] = useState(null);
    const [confirmBatchDocs, setConfirmBatchDocs] = useState(null);
    const [confirmBatchReprocess, setConfirmBatchReprocess] = useState(null);
    const [confirmBatchUsers, setConfirmBatchUsers] = useState(null);
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
        page: docPage, totalCount: docTotal, pageSize: docPageSize, goToPage: goToDocPage,
        fetchDocs,
        deleteDoc,
        batchDeleteDocs,
        batchReprocessDocs,
        batchDownloadDocs,
        handleViewChunks,
        processFiles,
    } = useDocuments();

    const {
        users,
        usersLoading,
        userSearch, setUserSearch,
        page: userPage, totalCount: userTotal, pageSize: userPageSize, goToUsersPage,
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
        batchDeleteUsers,
        // Bulk import (Excel)
        showBulkImportModal,
        bulkImportResult,
        bulkImportLoading,
        bulkImportProgress,
        openBulkImportModal,
        closeBulkImportModal,
        handleDownloadTemplate,
        handleBulkImport,
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
        } catch (err) {
            showApiError(toast, err, 'Belge silinemedi.');
        } finally {
            setDeletingDocId(null);
        }
    };

    const handleBatchDeleteDocs = (ids, exitSelectMode) => {
        setConfirmBatchDocs({ ids, exitSelectMode });
    };

    const confirmBatchDocsDelete = async () => {
        const { ids, exitSelectMode } = confirmBatchDocs;
        setConfirmBatchDocs(null);
        try {
            await batchDeleteDocs(ids);
            exitSelectMode?.();
        } catch { /* toast hook'ta atıldı */ }
    };

    // İndirme onaysız — direkt başlat
    const handleBatchDownloadDocs = (docs) => {
        batchDownloadDocs(docs);
    };

    // Yeniden işleme onaylı — geri alınamaz, kaynak yoğun
    const handleBatchReprocessDocs = (ids, exitSelectMode) => {
        setConfirmBatchReprocess({ ids, exitSelectMode });
    };

    const confirmBatchReprocessDocs = async () => {
        const { ids, exitSelectMode } = confirmBatchReprocess;
        setConfirmBatchReprocess(null);
        try {
            await batchReprocessDocs(ids);
            exitSelectMode?.();
        } catch { /* toast hook'ta atıldı */ }
    };

    // Çoklu kullanıcı silme — onaylı
    const handleBatchDeleteUsers = (ids, exitSelectMode) => {
        setConfirmBatchUsers({ ids, exitSelectMode });
    };

    const confirmBatchUsersDelete = async () => {
        const { ids, exitSelectMode } = confirmBatchUsers;
        setConfirmBatchUsers(null);
        try {
            await batchDeleteUsers(ids);
            exitSelectMode?.();
        } catch { /* toast hook'ta atıldı */ }
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
            showApiError(toast, err, 'Kullanıcı silinemedi.');
        } finally {
            setDeletingUserId(null);
        }
    };

    return (
        <div className="violet-drift" style={{ minHeight: '100vh' }}>
            <div className="admin-header glass" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '16px 28px', borderBottom: '1px solid var(--glass-border)', gap: '16px', flexWrap: 'wrap', position: 'sticky', top: 0, zIndex: 10 }}>
                <div className="gradient-beam" style={{ position: 'absolute', left: 0, right: 0, bottom: 0 }} />
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                    <button onClick={() => navigate('/chat')} className="btn btn-ghost btn-sm">
                        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                            <line x1="19" y1="12" x2="5" y2="12" /><polyline points="12 19 5 12 12 5" />
                        </svg>
                        Geri
                    </button>
                    <div style={{ width: '1px', height: '20px', background: 'var(--border)' }} />
                    <h1 style={{ fontSize: '20px', fontWeight: 700, margin: 0, letterSpacing: '-0.02em', color: 'var(--text-primary)' }}>Admin Panel</h1>
                </div>

                <div className="admin-tabs" style={{ display: 'flex', gap: '4px', padding: '5px', background: 'rgba(255,255,255,0.04)', border: '1px solid rgba(255,255,255,0.06)', borderRadius: '14px', backdropFilter: 'blur(20px) saturate(180%)' }}>
                    {[
                        { key: 'documents', label: 'Belgeler', count: docTotal, icon: <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /> },
                        { key: 'users', label: 'Kullanıcılar', count: userTotal, icon: <><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /></> },
                    ].map(t => (
                        <button key={t.key} onClick={() => setTab(t.key)} className={`btn admin-tab-btn ${tab === t.key ? 'btn-primary' : 'btn-ghost'}`} style={{ padding: '9px 16px', fontWeight: 600 }}>
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">{t.icon}</svg>
                            <span className="admin-tab-label">{t.label}</span>
                            <span style={{ fontSize: '11px', padding: '2px 8px', borderRadius: '8px', fontWeight: 700, background: tab === t.key ? 'rgba(255,255,255,0.22)' : 'rgba(255,255,255,0.08)', color: tab === t.key ? 'white' : 'rgba(255,255,255,0.55)', minWidth: '22px', textAlign: 'center' }}>{t.count}</span>
                        </button>
                    ))}
                </div>
            </div>

            <div className="admin-content" style={{ maxWidth: '1100px', margin: '0 auto', padding: '40px 28px' }}>
                <AnimatePresence mode="wait">
                <motion.div
                    key={tab}
                    initial={{ opacity: 0, y: 12 }}
                    animate={{ opacity: 1, y: 0 }}
                    exit={{ opacity: 0, y: -8 }}
                    transition={{ duration: 0.25, ease: 'easeOut' }}
                >
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
                            onBatchDelete={handleBatchDeleteDocs}
                            onBatchDownload={handleBatchDownloadDocs}
                            onBatchReprocess={handleBatchReprocessDocs}
                            deletingDocId={deletingDocId}
                            total={docTotal}
                            page={docPage}
                            pageSize={docPageSize}
                            onPageChange={goToDocPage}
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
                        onBulkImport={openBulkImportModal}
                        onEdit={openEditModal}
                        onDelete={handleDeleteUser}
                        onBatchDelete={handleBatchDeleteUsers}
                        deletingUserId={deletingUserId}
                        total={userTotal}
                        page={userPage}
                        pageSize={userPageSize}
                        onPageChange={goToUsersPage}
                    />
                )}
                </motion.div>
                </AnimatePresence>
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
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px', flexWrap: 'wrap' }}>
                                        <span style={{ fontSize: '11px', padding: '2px 8px', borderRadius: '6px', fontWeight: 500, background: 'rgba(var(--accent-rgb),0.15)', color: '#c4b5fd', border: '1px solid rgba(var(--accent-light-rgb),0.25)' }}>Chunk #{chunk.chunkIndex + 1}</span>
                                        {chunk.pageNumber != null && (
                                            <span style={{ fontSize: '11px', padding: '2px 8px', borderRadius: '6px', fontWeight: 500, background: 'rgba(168,85,247,0.15)', color: '#c4b5fd', border: '1px solid rgba(168,85,247,0.25)' }}>Sayfa {chunk.pageNumber}</span>
                                        )}
                                        <span style={{ fontSize: '12px', color: '#475569' }}>{chunk.content.length} karakter</span>
                                        {chunkImages.length > 0 && (
                                            <span style={{ fontSize: '11px', padding: '2px 8px', borderRadius: '6px', fontWeight: 500, background: 'rgba(34,197,94,0.15)', color: '#86efac', border: '1px solid rgba(34,197,94,0.2)' }}>{chunkImages.length} görsel</span>
                                        )}
                                    </div>
                                    {chunk.header && (
                                        <div style={{ fontSize: '11px', color: 'var(--text-muted)', marginBottom: '8px', fontStyle: 'italic', opacity: 0.85 }}>
                                            📂 {chunk.header}
                                        </div>
                                    )}
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

            <BulkImportUsersModal
                open={showBulkImportModal}
                onClose={closeBulkImportModal}
                onDownloadTemplate={handleDownloadTemplate}
                onImport={handleBulkImport}
                loading={bulkImportLoading}
                result={bulkImportResult}
                progress={bulkImportProgress}
            />

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

            {confirmBatchDocs && (
                <ConfirmDialog
                    title="Belgeleri Sil"
                    message={`${confirmBatchDocs.ids.length} belge kalıcı olarak silinecek. Emin misiniz?`}
                    confirmLabel="Sil"
                    onConfirm={confirmBatchDocsDelete}
                    onCancel={() => setConfirmBatchDocs(null)}
                />
            )}

            {confirmBatchReprocess && (
                <ConfirmDialog
                    title="Belgeleri Yeniden İşle"
                    message={`${confirmBatchReprocess.ids.length} belge yeniden işlenecek. Mevcut chunk'lar ve cache silinip baştan üretilecek. Sürebilir.`}
                    confirmLabel="Yeniden İşle"
                    onConfirm={confirmBatchReprocessDocs}
                    onCancel={() => setConfirmBatchReprocess(null)}
                />
            )}

            {confirmBatchUsers && (
                <ConfirmDialog
                    title="Kullanıcıları Sil"
                    message={`${confirmBatchUsers.ids.length} kullanıcı kalıcı olarak silinecek. Emin misiniz?`}
                    confirmLabel="Sil"
                    onConfirm={confirmBatchUsersDelete}
                    onCancel={() => setConfirmBatchUsers(null)}
                />
            )}
        </div>
    );
}
