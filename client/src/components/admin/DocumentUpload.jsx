import { departmentLabel } from '../../lib/format';

export default function DocumentUpload({ uploads, dragOver, onDragOver, onDragLeave, onDrop, onClick, fileInputRef, onFileChange, departments = [], departmentId, onDepartmentChange, isAdmin = false }) {
    return (
        <div style={{ marginBottom: '24px', borderRadius: '16px', padding: '20px', background: 'rgba(32, 26, 58, 0.55)', border: '1px solid rgba(var(--accent-light-rgb),0.14)', backdropFilter: 'blur(24px) saturate(160%)', WebkitBackdropFilter: 'blur(24px) saturate(160%)', boxShadow: '0 8px 28px -10px rgba(0,0,0,0.4), inset 0 1px 0 rgba(255,255,255,0.04)' }}>
            {/* Başlık — sola yaslı, ikon kutusu + büyük başlık */}
            <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '8px' }}>
                <div className="profile-section-icon" style={{ width: '38px', height: '38px', borderRadius: '11px', flexShrink: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'rgba(var(--accent-rgb),0.24)', color: '#d8ccff', border: '1px solid rgba(var(--accent-light-rgb),0.35)' }}>
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                        <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" /><polyline points="17 8 12 3 7 8" /><line x1="12" y1="3" x2="12" y2="15" />
                    </svg>
                </div>
                <h2 style={{ fontSize: '18px', fontWeight: 700, color: 'var(--text-primary)', margin: 0, letterSpacing: '-0.01em' }}>Belge Yükle</h2>
            </div>
            <p style={{ fontSize: '13.5px', color: 'var(--text-secondary)', margin: '0 0 14px' }}>
                Birden fazla dosya seçebilir veya sürükleyip bırakabilirsiniz.
            </p>

            {/* Departman seçici — belge bu departmana bağlanır (zorunlu) */}
            <div style={{ marginBottom: '14px' }}>
                <label style={{ display: 'block', fontSize: '12.5px', fontWeight: 600, color: 'var(--text-secondary)', marginBottom: '6px' }}>
                    Departman <span style={{ color: '#fca5a5' }}>*</span>
                </label>
                <select
                    value={departmentId || ''}
                    onChange={(e) => onDepartmentChange?.(e.target.value)}
                    disabled={departments.length === 0}
                    style={{ width: '100%', padding: '10px 12px', borderRadius: '10px', fontSize: '14px', background: 'var(--surface2)', color: 'var(--text-primary)', border: '1px solid var(--border)', cursor: departments.length === 0 ? 'not-allowed' : 'pointer' }}
                >
                    {/* option'lara açık renk: koyu temada tarayıcı varsayılanı okunmuyordu */}
                    <option value="" style={{ background: '#1c2034', color: '#e8e8f0' }}>— Departman seçin —</option>
                    {departments.map((d) => (
                        <option key={d.id} value={d.id} style={{ background: '#1c2034', color: '#e8e8f0' }}>{departmentLabel(d)}</option>
                    ))}
                </select>
                {departments.length === 0 && (
                    // Admin departmanı KENDİSİ oluşturur → ona "yöneticinle iletişime geç" demek yanlış.
                    <p style={{ fontSize: '12px', color: '#fca5a5', margin: '6px 0 0' }}>
                        {isAdmin
                            ? 'Henüz departman yok. Önce "Departmanlar" sekmesinden bir departman ekleyin.'
                            : 'Atanmış departmanınız yok. Belge yüklemek için yöneticinizle iletişime geçin.'}
                    </p>
                )}
            </div>

            <div onClick={onClick} onDragOver={onDragOver} onDragLeave={onDragLeave} onDrop={onDrop}
                style={{
                    borderRadius: '12px', padding: '32px 16px', textAlign: 'center', cursor: 'pointer',
                    border: `2px dashed ${dragOver ? 'var(--accent)' : 'var(--border)'}`,
                    background: dragOver ? 'rgba(var(--accent-rgb),0.06)' : 'var(--surface2)',
                }}>
                <div style={{ width: '48px', height: '48px', borderRadius: '12px', display: 'flex', alignItems: 'center', justifyContent: 'center', margin: '0 auto 12px', background: 'rgba(var(--accent-rgb),0.24)', border: '1px solid rgba(var(--accent-light-rgb),0.3)' }}>
                    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#d8ccff" strokeWidth="2">
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