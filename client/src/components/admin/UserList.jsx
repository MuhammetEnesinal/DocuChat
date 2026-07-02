import { useState, useCallback } from 'react';
import SearchInput from '../shared/SearchInput';
import { UserSkeleton } from '../shared/Skeleton';
import { formatDate } from '../../utils/format';
import IconButton from '../shared/IconButton';
import Spinner from '../shared/Spinner';
import Pagination from '../shared/Pagination';

export default function UserList({
    users, loading, search, onSearchChange,
    onAdd, onBulkImport, onEdit, onDelete, onBatchDelete,
    deletingUserId,
    total, page, pageSize, onPageChange,
}) {
    const [selectMode, setSelectMode] = useState(false);
    const [selectedIds, setSelectedIds] = useState(new Set());

    // Arama server-side yapılıyor — burada client filtre YOK, gelen sayfa aynen gösterilir.
    // Admin kullanıcılar seçilemez (silinemez)
    const selectableUsers = users.filter(u => !u.roles?.includes('Admin'));
    const allSelected = selectableUsers.length > 0 && selectableUsers.every(u => selectedIds.has(u.id));

    const toggleSelect = useCallback((id) => {
        setSelectedIds(prev => {
            const next = new Set(prev);
            if (next.has(id)) next.delete(id); else next.add(id);
            return next;
        });
    }, []);

    const toggleSelectAll = useCallback(() => {
        setSelectedIds(prev => {
            if (selectableUsers.every(u => prev.has(u.id))) return new Set();
            return new Set(selectableUsers.map(u => u.id));
        });
    }, [selectableUsers]);

    const exitSelectMode = useCallback(() => {
        setSelectMode(false);
        setSelectedIds(new Set());
    }, []);

    const handleBatchDelete = useCallback(() => {
        if (selectedIds.size === 0) return;
        onBatchDelete?.(Array.from(selectedIds), exitSelectMode);
    }, [selectedIds, onBatchDelete, exitSelectMode]);

    return (
      <>
        <div style={{ borderRadius: '16px', overflow: 'hidden', background: 'rgba(32, 26, 58, 0.55)', border: '1px solid rgba(var(--accent-light-rgb),0.14)', backdropFilter: 'blur(24px) saturate(160%)', WebkitBackdropFilter: 'blur(24px) saturate(160%)', boxShadow: '0 8px 28px -10px rgba(0,0,0,0.4), inset 0 1px 0 rgba(255,255,255,0.04)' }}>
            <div className="admin-list-header" style={{ padding: '16px 20px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', borderBottom: '1px solid var(--border)', flexWrap: 'wrap' }}>
                {/* Seçim modunda başlık gizlenir; yerine sol tarafta seçim kontrolleri gelir */}
                {!selectMode ? (
                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexShrink: 0 }}>
                        <h2 style={{ fontSize: '16.5px', fontWeight: 700, color: 'var(--text-primary)', margin: 0, letterSpacing: '-0.01em' }}>Kullanıcılar</h2>
                        <span style={{ fontSize: '12px', fontWeight: 600, padding: '3px 10px', borderRadius: '999px', background: 'rgba(var(--accent-rgb),0.16)', border: '1px solid rgba(var(--accent-light-rgb),0.25)', color: '#d8ccff', whiteSpace: 'nowrap' }}>{total ?? users.length} kullanıcı</span>
                    </div>
                ) : (
                    <div className="admin-select-info" style={{ display: 'flex', alignItems: 'center', gap: '10px', flexShrink: 0 }}>
                        <button onClick={toggleSelectAll} className="btn btn-ghost btn-sm" disabled={selectableUsers.length === 0}>
                            {allSelected ? 'Seçimi Kaldır' : 'Tümünü Seç'}
                        </button>
                        <span style={{ fontSize: '13px', color: 'var(--text-secondary)', fontWeight: 600, whiteSpace: 'nowrap' }}>{selectedIds.size} seçili</span>
                    </div>
                )}
                <div className={selectMode ? 'admin-select-actions' : 'admin-users-actions'} style={{ display: 'flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap', flex: '1 1 auto', justifyContent: 'flex-end', minWidth: 0 }}>
                    {selectMode ? (
                        <>
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
                            <button onClick={() => setSelectMode(true)} className="btn btn-ghost btn-sm" disabled={users.length === 0}>
                                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={{ marginRight: '4px' }}>
                                    <rect x="2" y="2" width="14" height="14" rx="2" />
                                    <path d="M8 22h12a2 2 0 0 0 2-2V8" />
                                </svg>
                                Çoklu Seç
                            </button>
                            <div className="admin-search" style={{ flex: '1 1 180px', maxWidth: '260px', minWidth: 0 }}>
                                <SearchInput value={search} onChange={onSearchChange} placeholder="Kullanıcı ara..." />
                            </div>
                            {onBulkImport && (
                                <button onClick={onBulkImport} className="admin-users-cta"
                                    title="Excel ile toplu kullanıcı yükle"
                                    style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '8px 14px', borderRadius: '10px', fontSize: '14px', fontWeight: 500, color: '#86efac', background: 'rgba(34,197,94,0.12)', border: '1px solid rgba(34,197,94,0.35)', cursor: 'pointer', whiteSpace: 'nowrap' }}
                                    onMouseEnter={(e) => { e.currentTarget.style.background = 'rgba(34,197,94,0.22)'; e.currentTarget.style.color = '#fff'; }}
                                    onMouseLeave={(e) => { e.currentTarget.style.background = 'rgba(34,197,94,0.12)'; e.currentTarget.style.color = '#86efac'; }}>
                                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                        <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                                        <polyline points="17 8 12 3 7 8" />
                                        <line x1="12" y1="3" x2="12" y2="15" />
                                    </svg>
                                    Excel Yükle
                                </button>
                            )}
                            <button onClick={onAdd} className="admin-users-cta"
                                style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '8px 14px', borderRadius: '10px', fontSize: '14px', fontWeight: 500, color: 'var(--text-primary)', background: 'var(--accent)', border: 'none', cursor: 'pointer', whiteSpace: 'nowrap' }}
                                onMouseEnter={(e) => e.currentTarget.style.opacity = '0.85'}
                                onMouseLeave={(e) => e.currentTarget.style.opacity = '1'}>
                                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                                    <line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" />
                                </svg>
                                Ekle
                            </button>
                        </>
                    )}
                </div>
            </div>

            {loading ? <UserSkeleton /> : users.length === 0 ? (
                <div style={{ textAlign: 'center', padding: '48px 16px' }}>
                    <p style={{ fontSize: '14px', color: 'var(--gray-light)' }}>{search ? 'Sonuç bulunamadı' : 'Kullanıcı bulunamadı'}</p>
                </div>
            ) : users.map((u) => {
                const isAdmin = u.roles?.includes('Admin');
                const isSelected = selectMode && selectedIds.has(u.id);
                return (
                    <div key={u.id}
                        style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: '8px 12px', padding: '14px clamp(12px, 2.5vw, 20px)', borderBottom: '1px solid var(--border)', transition: 'background 0.15s', background: isSelected ? 'rgba(var(--accent-rgb),0.08)' : 'transparent' }}
                        onMouseEnter={(e) => { if (!isSelected) e.currentTarget.style.background = 'var(--surface2)'; }}
                        onMouseLeave={(e) => { e.currentTarget.style.background = isSelected ? 'rgba(var(--accent-rgb),0.08)' : 'transparent'; }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '12px', minWidth: 0, flex: '1 1 220px' }}>
                            {selectMode && (
                                <input
                                    type="checkbox"
                                    checked={selectedIds.has(u.id)}
                                    onChange={() => toggleSelect(u.id)}
                                    disabled={isAdmin}
                                    title={isAdmin ? 'Admin kullanıcı seçilemez' : ''}
                                    style={{ width: '16px', height: '16px', cursor: isAdmin ? 'not-allowed' : 'pointer', accentColor: 'var(--accent-light)', flexShrink: 0 }}
                                />
                            )}
                            <div style={{ width: '36px', height: '36px', borderRadius: '50%', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0, fontWeight: 600, fontSize: '14px', background: isAdmin ? 'rgba(var(--accent-rgb),0.24)' : 'rgba(148,163,184,0.16)', color: isAdmin ? '#d8ccff' : 'var(--text-secondary)' }}>
                                {u.fullName?.charAt(0)?.toUpperCase() || '?'}
                            </div>
                            <div style={{ minWidth: 0 }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                                    <p style={{ fontSize: '14px', fontWeight: 500, color: 'var(--text-primary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', margin: 0 }}>{u.fullName}</p>
                                    {isAdmin && <span style={{ fontSize: '11px', padding: '2px 8px', borderRadius: '6px', fontWeight: 500, background: 'rgba(var(--accent-rgb),0.15)', color: '#c4b5fd', border: '1px solid rgba(var(--accent-light-rgb),0.25)', flexShrink: 0 }}>Admin</span>}
                                </div>
                                <p style={{ fontSize: '12px', color: 'var(--gray-light)', marginTop: '2px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{u.email} · {formatDate(u.createdAt)}</p>
                            </div>
                        </div>
                        {!isAdmin && !selectMode && (
                            <div className="admin-row-actions" style={{ display: 'flex', gap: '4px', flexShrink: 0, marginLeft: 'auto' }}>
                                <IconButton onClick={() => onEdit(u)} title="Düzenle" hoverColor="var(--accent-light)" hoverBg="rgba(var(--accent-light-rgb),0.1)">
                                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                        <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
                                        <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
                                    </svg>
                                </IconButton>
                                <IconButton onClick={() => deletingUserId !== u.id && onDelete(u.id, u.fullName)} title={deletingUserId === u.id ? 'Siliniyor...' : 'Sil'} hoverColor="#f87171" hoverBg="rgba(248,113,113,0.1)" disabled={deletingUserId === u.id}>
                                    {deletingUserId === u.id
                                        ? <Spinner size={15} />
                                        : <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                            <polyline points="3 6 5 6 21 6" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
                                          </svg>
                                    }
                                </IconButton>
                            </div>
                        )}
                    </div>
                );
            })}
        </div>
        {!loading && (
            <Pagination page={page} pageSize={pageSize} totalCount={total ?? users.length} onPageChange={onPageChange} />
        )}
      </>
    );
}
