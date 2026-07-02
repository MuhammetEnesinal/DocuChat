export default function DocumentUpload({ uploads, dragOver, onDragOver, onDragLeave, onDrop, onClick, fileInputRef, onFileChange }) {
    return (
        <div style={{ marginBottom: '24px', borderRadius: '16px', padding: '20px', background: 'rgba(32, 26, 58, 0.55)', border: '1px solid rgba(var(--accent-light-rgb),0.14)', backdropFilter: 'blur(24px) saturate(160%)', WebkitBackdropFilter: 'blur(24px) saturate(160%)', boxShadow: '0 8px 28px -10px rgba(0,0,0,0.4), inset 0 1px 0 rgba(255,255,255,0.04)' }}>
            <h2 style={{ fontSize: '15px', fontWeight: 600, color: 'var(--text-primary)', marginBottom: '6px' }}>Belge Yükle</h2>
            <p style={{ fontSize: '13px', color: 'var(--gray-light)', marginBottom: '16px' }}>
                Birden fazla dosya seçebilir veya sürükleyip bırakabilirsiniz.
            </p>
            <div onClick={onClick} onDragOver={onDragOver} onDragLeave={onDragLeave} onDrop={onDrop}
                style={{
                    borderRadius: '12px', padding: '32px 16px', textAlign: 'center', cursor: 'pointer',
                    border: `2px dashed ${dragOver ? 'var(--accent)' : 'var(--border)'}`,
                    background: dragOver ? 'rgba(var(--accent-rgb),0.06)' : 'var(--surface2)',
                }}>
                <div style={{ width: '48px', height: '48px', borderRadius: '12px', display: 'flex', alignItems: 'center', justifyContent: 'center', margin: '0 auto 12px', background: 'rgba(var(--accent-rgb),0.15)' }}>
                    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="var(--accent-light)" strokeWidth="2">
                        <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" /><polyline points="17 8 12 3 7 8" /><line x1="12" y1="3" x2="12" y2="15" />
                    </svg>
                </div>
                <p style={{ fontSize: '14px', fontWeight: 500, color: 'var(--text-primary)', marginBottom: '4px' }}>Dosya seçin veya buraya sürükleyin</p>
                <p style={{ fontSize: '12px', color: 'var(--gray-light)' }}>PDF, DOC, DOCX, XLSX, CSV · Maks. 50 MB</p>
            </div>
            <input ref={fileInputRef} type="file" accept=".pdf,.doc,.docx,.xlsx,.csv" style={{ display: 'none' }} multiple onChange={onFileChange} />

            {uploads.length > 0 && (
                <div style={{ marginTop: '12px', display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    {uploads.map((u) => (
                        <div key={u.id} style={{
                            padding: '12px 16px', borderRadius: '12px',
                            background: u.status === 'error' ? 'rgba(239,68,68,0.08)' : 'rgba(var(--accent-rgb),0.08)',
                            border: `1px solid ${u.status === 'error' ? 'rgba(239,68,68,0.2)' : 'rgba(var(--accent-light-rgb),0.25)'}`,
                        }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '6px' }}>
                                <span style={{ fontSize: '12px', fontWeight: 500, color: u.status === 'error' ? '#fca5a5' : '#c4b5fd', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: '70%' }}>{u.name}</span>
                                <span style={{ fontSize: '12px', color: u.status === 'error' ? '#fca5a5' : '#c4b5fd' }}>
                                    {u.status === 'error' ? 'Hata' : u.status === 'done' ? '✓' : `%${u.progress}`}
                                </span>
                            </div>
                            {u.status === 'uploading' && (
                                <div style={{ height: '4px', borderRadius: '4px', background: 'rgba(var(--accent-rgb),0.2)', overflow: 'hidden' }}>
                                    <div style={{ width: `${u.progress}%`, height: '100%', background: 'var(--accent)', borderRadius: '4px', transition: 'width 0.3s' }} />
                                </div>
                            )}
                            {u.status === 'error' && u.error && (
                                <p style={{ fontSize: '12px', marginTop: '4px', color: '#fca5a5' }}>{u.error}</p>
                            )}
                        </div>
                    ))}
                </div>
            )}
        </div>
    );
}