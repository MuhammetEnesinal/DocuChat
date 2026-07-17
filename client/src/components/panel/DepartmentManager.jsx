import { useState, useCallback } from 'react';
import IconButton from '../shared/IconButton';
import Spinner from '../shared/Spinner';

// Admin departman yönetimi: ekle / düzenle / sil / çoklu sil.
// DİKKAT: Modal burada RENDER EDİLMEZ — bu kartta backdrop-filter var, o da position:fixed için
// containing block yaratıyor ve kartın overflow:hidden'ı modal'ı kırpıyordu. Modal ve silme
// onayları ManagementPanel.jsx'te sayfa seviyesinde açılır (UserModal ile aynı desen).
export default function DepartmentManager({ departments, loading, onAddClick, onEditClick, onDelete, onBatchDelete }) {
    const [selectMode, setSelectMode] = useState(false);
    const [selectedIds, setSelectedIds] = useState(new Set());

    // Bağlı kullanıcı/belgesi olan departman silinemez → seçilemez de olmalı (sebebi görünsün).
    const isLocked = (d) => d.userCount > 0 || d.documentCount > 0;
    const selectable = departments.filter(d => !isLocked(d));
    const allSelected = selectable.length > 0 && selectable.every(d => selectedIds.has(d.id));

    const toggleSelect = useCallback((id) => {
        setSelectedIds(prev => {
            const next = new Set(prev);
            next.has(id) ? next.delete(id) : next.add(id);
            return next;
        });
    }, []);

    const toggleSelectAll = useCallback(() => {
        setSelectedIds(allSelected ? new Set() : new Set(selectable.map(d => d.id)));
    }, [allSelected, selectable]);

    const exitSelectMode = useCallback(() => {
        setSelectMode(false);
        setSelectedIds(new Set());
    }, []);

    const handleBatchDelete = useCallback(() => {
        if (selectedIds.size === 0) return;
        onBatchDelete?.(Array.from(selectedIds), exitSelectMode);
    }, [selectedIds, onBatchDelete, exitSelectMode]);

    return (
        <div style={{ borderRadius: '16px', overflow: 'hidden', background: 'rgba(32, 26, 58, 0.55)', border: '1px solid rgba(var(--accent-light-rgb),0.14)', backdropFilter: 'blur(24px) saturate(160%)', WebkitBackdropFilter: 'blur(24px) saturate(160%)', boxShadow: '0 8px 28px -10px rgba(0,0,0,0.4), inset 0 1px 0 rgba(255,255,255,0.04)' }}>
            <div className="panel-list-header" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', flexWrap: 'wrap', padding: '16px 20px', borderBottom: '1px solid var(--border)' }}>
                {!selectMode ? (
                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexShrink: 0 }}>
                        <h2 style={{ fontSize: '16.5px', fontWeight: 700, color: 'var(--text-primary)', margin: 0 }}>Departmanlar</h2>
                        <span style={{ fontSize: '12px', fontWeight: 600, padding: '3px 10px', borderRadius: '999px', background: 'rgba(var(--accent-rgb),0.16)', border: '1px solid rgba(var(--accent-light-rgb),0.25)', color: '#d8ccff', whiteSpace: 'nowrap' }}>{departments.length} departman</span>
                    </div>
                ) : (
                    <div className="panel-select-info" style={{ display: 'flex', alignItems: 'center', gap: '10px', flexShrink: 0 }}>
                        <button onClick={toggleSelectAll} className="btn btn-ghost btn-sm" disabled={selectable.length === 0}>
                            {allSelected ? 'Seçimi Kaldır' : 'Tümünü Seç'}
                        </button>
                        <span style={{ fontSize: '13px', color: 'var(--text-secondary)', fontWeight: 600, whiteSpace: 'nowrap' }}>{selectedIds.size} seçili</span>
                    </div>
                )}

                {/* Sınıflar index.css'teki dar-ekran kurallarını devralır (UserList ile aynı) */}
                <div className={selectMode ? 'panel-select-actions' : 'panel-users-actions'}
                    style={{ display: 'flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap', flex: '1 1 auto', justifyContent: 'flex-end', minWidth: 0 }}>
                    {selectMode ? (
                        <>
                            <button onClick={handleBatchDelete} disabled={selectedIds.size === 0}
                                style={{ padding: '6px 14px', borderRadius: '8px', fontSize: '13px', fontWeight: 600, background: selectedIds.size === 0 ? 'rgba(248,113,113,0.1)' : 'rgba(248,113,113,0.2)', color: '#f87171', border: '1px solid rgba(248,113,113,0.3)', cursor: selectedIds.size === 0 ? 'not-allowed' : 'pointer', opacity: selectedIds.size === 0 ? 0.5 : 1, display: 'flex', alignItems: 'center', gap: '6px' }}>
                                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="3 6 5 6 21 6" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" /></svg>
                                Sil ({selectedIds.size})
                            </button>
                            <button onClick={exitSelectMode}
                                style={{ padding: '6px 14px', borderRadius: '8px', fontSize: '13px', fontWeight: 600, background: 'rgba(255,255,255,0.08)', color: 'var(--text-secondary)', border: '1px solid rgba(255,255,255,0.18)', cursor: 'pointer' }}>
                                Vazgeç
                            </button>
                        </>
                    ) : (
                        <>
                            <button onClick={() => setSelectMode(true)} className="btn btn-ghost btn-sm" disabled={selectable.length === 0}
                                title={selectable.length === 0 ? 'Silinebilecek (boş) departman yok' : ''}>
                                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={{ marginRight: '4px' }}>
                                    <rect x="2" y="2" width="14" height="14" rx="2" />
                                    <path d="M8 22h12a2 2 0 0 0 2-2V8" />
                                </svg>
                                Çoklu Seç
                            </button>
                            <button onClick={onAddClick} className="btn btn-primary btn-sm panel-users-cta" style={{ fontWeight: 600, whiteSpace: 'nowrap' }}>
                                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round">
                                    <line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" />
                                </svg>
                                Departman Ekle
                            </button>
                        </>
                    )}
                </div>
            </div>

            {loading ? (
                <div style={{ textAlign: 'center', padding: '40px' }}><Spinner size={22} /></div>
            ) : departments.length === 0 ? (
                <div style={{ textAlign: 'center', padding: '40px 16px' }}>
                    <p style={{ fontSize: '14px', color: 'var(--gray-light)' }}>Henüz departman yok. "Departman Ekle" ile başlayın.</p>
                </div>
            ) : departments.map((d) => {
                const locked = isLocked(d);
                const isSelected = selectMode && selectedIds.has(d.id);
                return (
                    <div key={d.id} style={{ display: 'flex', alignItems: 'center', gap: '10px', padding: '14px clamp(12px, 2.5vw, 20px)', borderBottom: '1px solid var(--border)', background: isSelected ? 'rgba(var(--accent-rgb),0.08)' : 'transparent' }}>
                        {selectMode && (
                            <input
                                type="checkbox"
                                checked={selectedIds.has(d.id)}
                                onChange={() => toggleSelect(d.id)}
                                disabled={locked}
                                title={locked ? 'Bağlı kullanıcı veya belge var — silinemez' : ''}
                                style={{ width: '16px', height: '16px', cursor: locked ? 'not-allowed' : 'pointer', accentColor: 'var(--accent-light)', flexShrink: 0 }}
                            />
                        )}
                        <div style={{ flex: 1, minWidth: 0 }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                                <p style={{ fontSize: '14px', fontWeight: 500, color: 'var(--text-primary)', margin: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{d.name}</p>
                                {/* Kod rozeti — Excel'de bu kod birebir yazılacağı için görünür olmalı */}
                                <span style={{ fontSize: '11px', fontWeight: 700, padding: '2px 8px', borderRadius: '6px', fontFamily: "'JetBrains Mono', monospace", background: 'rgba(var(--accent-rgb),0.15)', color: '#c4b5fd', border: '1px solid rgba(var(--accent-light-rgb),0.25)', flexShrink: 0 }}>{d.code}</span>
                            </div>
                            <p style={{ fontSize: '12px', color: 'var(--gray-light)', marginTop: '2px' }}>
                                {d.userCount} kullanıcı · {d.documentCount} belge
                            </p>
                        </div>
                        {!selectMode && (
                            <>
                                <IconButton onClick={() => onEditClick(d)} title="Düzenle" hoverColor="var(--accent-light)" hoverBg="rgba(var(--accent-light-rgb),0.1)">
                                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                        <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
                                        <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
                                    </svg>
                                </IconButton>
                                <IconButton onClick={() => onDelete(d.id, d.name)} title="Sil" hoverColor="#f87171" hoverBg="rgba(248,113,113,0.1)">
                                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                        <polyline points="3 6 5 6 21 6" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
                                    </svg>
                                </IconButton>
                            </>
                        )}
                    </div>
                );
            })}
        </div>
    );
}
