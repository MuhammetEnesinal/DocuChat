import { useState, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { SessionSkeleton } from '../shared/Skeleton';
import SidebarButton from './SidebarButton';

export default function ChatSidebar({
    sessions, sessionsLoading, activeSession,
    editingSessionId, editingTitle, deletingSessionId, renamingSessionId,
    collapsed, onToggleCollapse,
    onNewChat, onLoadSession,
    onStartRename, onCommitRename, onSetEditingTitle, onSetEditingSessionId,
    onDeleteSession, onBatchDeleteSessions, user, onLogout,
    // Archive / Pin
    showArchived, archivedCount, busy,
    onToggleArchived, onArchive, onUnarchive, onPin, onUnpin,
    onBatchArchiveSessions, onBatchUnarchiveSessions,
}) {
    // Helper: spesifik action için busy mi?
    const isBusy = (sessionId, action) => busy?.id === sessionId && busy?.action === action;
    const navigate = useNavigate();
    const [selectMode, setSelectMode] = useState(false);
    const [selectedIds, setSelectedIds] = useState(new Set());

    const allSelected = sessions.length > 0 && sessions.every(s => selectedIds.has(s.id));

    const toggleSelect = useCallback((id) => {
        setSelectedIds(prev => {
            const next = new Set(prev);
            if (next.has(id)) next.delete(id); else next.add(id);
            return next;
        });
    }, []);

    const toggleSelectAll = useCallback(() => {
        setSelectedIds(prev => {
            if (sessions.every(s => prev.has(s.id))) return new Set();
            return new Set(sessions.map(s => s.id));
        });
    }, [sessions]);

    const exitSelectMode = useCallback(() => {
        setSelectMode(false);
        setSelectedIds(new Set());
    }, []);

    const handleBatchDelete = useCallback(() => {
        if (selectedIds.size === 0) return;
        onBatchDeleteSessions?.(Array.from(selectedIds), exitSelectMode);
    }, [selectedIds, onBatchDeleteSessions, exitSelectMode]);

    const handleBatchArchive = useCallback(async () => {
        if (selectedIds.size === 0) return;
        await onBatchArchiveSessions?.(Array.from(selectedIds));
        exitSelectMode();
    }, [selectedIds, onBatchArchiveSessions, exitSelectMode]);

    const handleBatchUnarchive = useCallback(async () => {
        if (selectedIds.size === 0) return;
        await onBatchUnarchiveSessions?.(Array.from(selectedIds));
        exitSelectMode();
    }, [selectedIds, onBatchUnarchiveSessions, exitSelectMode]);

    if (collapsed) {
        return (
            <div style={{ width: '60px', background: 'rgba(50, 45, 90, 0.32)', backdropFilter: 'blur(40px) saturate(180%)', WebkitBackdropFilter: 'blur(40px) saturate(180%)', borderRight: '1px solid rgba(var(--accent-light-rgb),0.18)', display: 'flex', flexDirection: 'column', alignItems: 'center', padding: '16px 0', gap: '14px', flexShrink: 0 }}>
                <div style={{ width: '36px', height: '36px', borderRadius: '10px', display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'var(--gradient-accent)', marginBottom: '4px', boxShadow: '0 6px 20px -6px rgba(99,102,241,0.6)' }}>
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
                        <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                    </svg>
                </div>
                <button onClick={onToggleCollapse} title="Paneli Genişlet"
                    style={{ width: '36px', height: '36px', display: 'flex', alignItems: 'center', justifyContent: 'center', borderRadius: '9px', color: '#ffffff', background: 'transparent', border: 'none', cursor: 'pointer', transition: 'background 0.15s' }}
                    onMouseEnter={(e) => { e.currentTarget.style.background = 'rgba(255,255,255,0.08)'; }}
                    onMouseLeave={(e) => { e.currentTarget.style.background = 'transparent'; }}>
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2">
                        <line x1="3" y1="12" x2="21" y2="12" /><line x1="3" y1="6" x2="21" y2="6" /><line x1="3" y1="18" x2="21" y2="18" />
                    </svg>
                </button>
                <button onClick={onNewChat} title="Yeni Sohbet"
                    style={{ width: '36px', height: '36px', display: 'flex', alignItems: 'center', justifyContent: 'center', borderRadius: '9px', color: '#ffffff', background: 'transparent', border: 'none', cursor: 'pointer', transition: 'background 0.15s' }}
                    onMouseEnter={(e) => { e.currentTarget.style.background = 'rgba(255,255,255,0.08)'; }}
                    onMouseLeave={(e) => { e.currentTarget.style.background = 'transparent'; }}>
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4">
                        <line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" />
                    </svg>
                </button>
            </div>
        );
    }

    return (
        <>
            {/* Mobil overlay */}
            <div className="chat-sidebar-overlay" onClick={onToggleCollapse} />

            <div className="chat-sidebar" style={{ width: '280px', background: 'rgba(50, 45, 90, 0.32)', backdropFilter: 'blur(40px) saturate(180%)', WebkitBackdropFilter: 'blur(40px) saturate(180%)', borderRight: '1px solid rgba(var(--accent-light-rgb),0.18)', display: 'flex', flexDirection: 'column', flexShrink: 0, transition: 'width 0.2s', boxShadow: 'inset -1px 0 0 rgba(255,255,255,0.04), 4px 0 30px -10px rgba(0,0,0,0.4)' }}>
                {/* Logo + Daralt */}
                <div style={{ position: 'relative', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '18px 16px', borderBottom: '1px solid var(--glass-border)' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '11px', minWidth: 0 }}>
                        <div style={{ width: '38px', height: '38px', borderRadius: '12px', display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'var(--gradient-accent)', flexShrink: 0, boxShadow: '0 8px 22px -6px rgba(99,102,241,0.6), inset 0 1px 0 rgba(255,255,255,0.28)' }}>
                            <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round">
                                <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                            </svg>
                        </div>
                        <span className="gradient-text" style={{ fontWeight: 700, fontSize: '16.5px', letterSpacing: '-0.01em', lineHeight: 1, display: 'flex', alignItems: 'center' }}>DocuChat</span>
                    </div>
                    <button onClick={onToggleCollapse} title="Paneli Daralt" aria-label="Paneli Daralt"
                        style={{ position: 'absolute', right: '12px', top: '50%', transform: 'translateY(-50%)', width: '32px', height: '32px', display: 'flex', alignItems: 'center', justifyContent: 'center', borderRadius: '9px', color: '#e2e7ef', background: 'transparent', border: 'none', cursor: 'pointer', transition: 'all 0.15s' }}
                        onMouseEnter={(e) => { e.currentTarget.style.background = 'rgba(255,255,255,0.08)'; e.currentTarget.style.color = '#fff'; }}
                        onMouseLeave={(e) => { e.currentTarget.style.background = 'transparent'; e.currentTarget.style.color = '#e2e7ef'; }}>
                        <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                            <line x1="3" y1="12" x2="21" y2="12" /><line x1="3" y1="6" x2="21" y2="6" /><line x1="3" y1="18" x2="21" y2="18" />
                        </svg>
                    </button>
                </div>

                {/* Yeni Sohbet */}
                <div style={{ padding: '14px' }}>
                    <button onClick={onNewChat} className="btn-gradient" style={{ width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px', padding: '12px 14px', borderRadius: '12px', fontSize: '14px', fontWeight: 600, cursor: 'pointer', letterSpacing: '0.01em' }}>
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                            <line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" />
                        </svg>
                        Yeni Sohbet
                    </button>
                </div>

                {/* Çoklu seçim toolbar */}
                {sessions.length > 0 && (
                    <div style={{ padding: '0 14px 8px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '6px', fontSize: '12px' }}>
                        {selectMode ? (
                            <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                {/* Üst satır: Tümü/Hiçbiri toggle + seçili sayısı */}
                                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '8px' }}>
                                    <button onClick={toggleSelectAll}
                                        style={{ background: 'linear-gradient(135deg, rgba(var(--accent-light-rgb),0.35), rgba(var(--accent-rgb),0.25))', border: '1px solid rgba(var(--accent-light-rgb),0.65)', color: '#ffffff', cursor: 'pointer', padding: '6px 14px', fontSize: '12px', fontWeight: 700, borderRadius: '8px', whiteSpace: 'nowrap', boxShadow: '0 0 8px rgba(var(--accent-light-rgb),0.25)' }}>
                                        {allSelected ? 'Hiçbiri' : 'Tümü'}
                                    </button>
                                    <span style={{ color: 'var(--text-secondary)', fontSize: '12px', fontWeight: 600, whiteSpace: 'nowrap' }}>{selectedIds.size} seçili</span>
                                </div>
                                {/* Alt: aksiyonlar. Arşiv view'da "Arşivden Çıkar" üstte tam genişlik. */}
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                                    {showArchived && (
                                        <button onClick={handleBatchUnarchive} disabled={selectedIds.size === 0}
                                            title="Seçili sohbetleri arşivden çıkar"
                                            style={{ width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center', background: selectedIds.size === 0 ? 'rgba(34,197,94,0.15)' : 'linear-gradient(135deg, #22c55e, #16a34a)', border: '1px solid ' + (selectedIds.size === 0 ? 'rgba(34,197,94,0.5)' : '#22c55e'), color: '#ffffff', cursor: selectedIds.size === 0 ? 'not-allowed' : 'pointer', padding: '8px 10px', fontSize: '12px', fontWeight: 700, borderRadius: '8px', opacity: selectedIds.size === 0 ? 0.7 : 1, transition: 'all 0.15s' }}>
                                            Arşivden Çıkar
                                        </button>
                                    )}
                                    <div style={{ display: 'flex', gap: '6px' }}>
                                        {!showArchived && (
                                            <button onClick={handleBatchArchive} disabled={selectedIds.size === 0}
                                                title={`${selectedIds.size} sohbeti arşive taşı`}
                                                style={{ flex: 1, minWidth: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', background: selectedIds.size === 0 ? 'rgba(251,146,60,0.15)' : 'linear-gradient(135deg, #f97316, #ea580c)', border: '1px solid ' + (selectedIds.size === 0 ? 'rgba(251,146,60,0.5)' : '#f97316'), color: '#ffffff', cursor: selectedIds.size === 0 ? 'not-allowed' : 'pointer', padding: '7px 8px', fontSize: '11.5px', fontWeight: 700, borderRadius: '8px', opacity: selectedIds.size === 0 ? 0.7 : 1, transition: 'all 0.15s' }}>
                                                Arşivle
                                            </button>
                                        )}
                                        <button onClick={handleBatchDelete} disabled={selectedIds.size === 0}
                                            title={`${selectedIds.size} sohbeti sil`}
                                            style={{ flex: 1, minWidth: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', background: selectedIds.size === 0 ? 'rgba(248,113,113,0.15)' : 'linear-gradient(135deg, #ef4444, #dc2626)', border: '1px solid ' + (selectedIds.size === 0 ? 'rgba(248,113,113,0.65)' : '#ef4444'), color: '#ffffff', cursor: selectedIds.size === 0 ? 'not-allowed' : 'pointer', padding: '7px 8px', fontSize: '11.5px', fontWeight: 700, borderRadius: '8px', opacity: selectedIds.size === 0 ? 0.7 : 1, boxShadow: selectedIds.size === 0 ? 'none' : '0 0 10px rgba(239,68,68,0.4)', transition: 'all 0.15s' }}>
                                            Sil
                                        </button>
                                        <button onClick={exitSelectMode}
                                            style={{ flex: 1, minWidth: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'rgba(255,255,255,0.12)', border: '1px solid rgba(255,255,255,0.35)', color: '#ffffff', cursor: 'pointer', padding: '7px 8px', fontSize: '11.5px', fontWeight: 700, borderRadius: '8px', transition: 'all 0.15s' }}>
                                            Vazgeç
                                        </button>
                                    </div>
                                </div>
                            </div>
                        ) : (
                            <button onClick={() => setSelectMode(true)}
                                style={{ background: 'none', border: 'none', color: 'var(--text-muted)', cursor: 'pointer', padding: '4px 6px', fontSize: '11.5px', display: 'flex', alignItems: 'center', gap: '4px' }}>
                                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                    <rect x="2" y="2" width="14" height="14" rx="2" />
                                    <path d="M8 22h12a2 2 0 0 0 2-2V8" />
                                </svg>
                                Çoklu Seç
                            </button>
                        )}
                    </div>
                )}

                {/* Arşiv görünümü başlığı — hangi listede olduğun tek bakışta belli olsun */}
                {showArchived && (
                    <div style={{ padding: '2px 16px 8px', display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <div style={{ flex: 1, height: '1px', background: 'rgba(251,146,60,0.3)' }} />
                        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="#fb923c" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                            <polyline points="21 8 21 21 3 21 3 8" /><rect x="1" y="3" width="22" height="5" /><line x1="10" y1="12" x2="14" y2="12" />
                        </svg>
                        <span style={{ fontSize: '11px', fontWeight: 700, letterSpacing: '0.1em', textTransform: 'uppercase', color: '#fb923c', whiteSpace: 'nowrap' }}>Arşiv</span>
                        <div style={{ flex: 1, height: '1px', background: 'rgba(251,146,60,0.3)' }} />
                    </div>
                )}

                {/* Session listesi */}
                <div role="list" style={{ flex: 1, overflowY: 'auto', padding: '0 10px 12px', display: 'flex', flexDirection: 'column', gap: '3px' }}>
                    {sessionsLoading ? <SessionSkeleton /> : sessions.length === 0 ? (
                        <p style={{ fontSize: '12px', textAlign: 'center', marginTop: '16px', color: 'var(--gray-light)' }}>Henüz sohbet yok</p>
                    ) : null}
                    {sessions.map((s) => {
                        const isSelected = selectMode && selectedIds.has(s.id);
                        const baseBg = isSelected
                            ? 'rgba(var(--accent-rgb),0.18)'
                            : activeSession?.id === s.id
                                ? 'linear-gradient(135deg, rgba(var(--accent-rgb),0.22) 0%, rgba(99,102,241,0.16) 100%)'
                                : 'transparent';
                        return (
                        <div key={s.id}
                            role="listitem"
                            tabIndex={0}
                            aria-label={s.title || 'Sohbet'}
                            aria-current={activeSession?.id === s.id ? 'true' : undefined}
                            onClick={() => selectMode ? toggleSelect(s.id) : onLoadSession(s)}
                            onKeyDown={(e) => {
                                if (selectMode) {
                                    if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); toggleSelect(s.id); }
                                    return;
                                }
                                if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onLoadSession(s); }
                                else if (e.key === 'Delete' || e.key === 'Backspace') { e.preventDefault(); onDeleteSession(s.id); }
                                else if (e.key === 'F2') { e.preventDefault(); onStartRename(s); }
                            }}
                            className="group"
                            style={{
                                display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                                padding: '11px 12px', borderRadius: '12px', cursor: 'pointer', transition: 'all 0.15s',
                                background: baseBg,
                                border: isSelected
                                    ? '1px solid rgba(var(--accent-light-rgb),0.45)'
                                    : activeSession?.id === s.id ? '1px solid rgba(var(--accent-light-rgb),0.35)' : '1px solid transparent',
                                boxShadow: activeSession?.id === s.id ? '0 4px 14px -6px rgba(var(--accent-rgb),0.4), inset 0 1px 0 rgba(255,255,255,0.05)' : 'none',
                                outline: 'none',
                            }}
                            onFocus={(e) => { if (!isSelected && activeSession?.id !== s.id) e.currentTarget.style.background = 'rgba(255,255,255,0.04)'; }}
                            onBlur={(e) => { if (!isSelected && activeSession?.id !== s.id) e.currentTarget.style.background = 'transparent'; }}
                            onMouseEnter={(e) => { if (!isSelected && activeSession?.id !== s.id) e.currentTarget.style.background = 'rgba(255,255,255,0.04)'; }}
                            onMouseLeave={(e) => { if (!isSelected && activeSession?.id !== s.id) e.currentTarget.style.background = 'transparent'; }}>
                            {selectMode && (
                                <input
                                    type="checkbox"
                                    checked={selectedIds.has(s.id)}
                                    onChange={() => toggleSelect(s.id)}
                                    onClick={(e) => e.stopPropagation()}
                                    style={{ width: '14px', height: '14px', cursor: 'pointer', accentColor: 'var(--accent-light)', marginRight: '8px', flexShrink: 0 }}
                                />
                            )}
                            {editingSessionId === s.id ? (
                                <input value={editingTitle}
                                    onChange={(e) => onSetEditingTitle(e.target.value)}
                                    onBlur={() => onCommitRename(s.id)}
                                    onKeyDown={(e) => { if (e.key === 'Enter') onCommitRename(s.id); if (e.key === 'Escape') onSetEditingSessionId(null); }}
                                    onClick={(e) => e.stopPropagation()}
                                    maxLength={60}
                                    style={{ flex: 1, fontSize: '14px', background: 'transparent', outline: 'none', border: 'none', borderBottom: '1px solid var(--accent-light)', color: '#fff' }}
                                    autoFocus
                                />
                            ) : (
                                <span style={{ fontSize: '13.5px', fontWeight: activeSession?.id === s.id ? 600 : 500, flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', color: activeSession?.id === s.id ? '#fff' : 'var(--text-secondary)', display: 'flex', alignItems: 'center', gap: '6px' }}>
                                    {s.isPinned && !showArchived && (
                                        <span title="Sabitli" style={{ color: '#fbbf24', flexShrink: 0, fontSize: '10px' }}>📌</span>
                                    )}
                                    <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', flex: 1 }}>
                                        {s.title || 'Sohbet'}
                                    </span>
                                </span>
                            )}
                            <div style={{ display: 'flex', alignItems: 'center', gap: '4px', flexShrink: 0, marginLeft: '6px' }}
                                className="session-actions">
                                {/* Pin icon — pinli session'larda her zaman görünür (sarı), diğerlerinde hover */}
                                {!showArchived && (
                                    <button
                                        onClick={(e) => { e.stopPropagation(); if (!isBusy(s.id, 'pin')) (s.isPinned ? onUnpin?.(s.id) : onPin?.(s.id)); }}
                                        disabled={isBusy(s.id, 'pin')}
                                        aria-label={s.isPinned ? 'Sabiti kaldır' : 'Sabitle'}
                                        title={s.isPinned ? 'Sabiti kaldır' : 'Sabitle'}
                                        style={{ width: '26px', height: '26px', display: 'flex', alignItems: 'center', justifyContent: 'center', borderRadius: '8px', color: s.isPinned ? '#fbbf24' : 'var(--text-muted)', background: s.isPinned ? 'rgba(251,191,36,0.12)' : 'transparent', border: 'none', cursor: isBusy(s.id, 'pin') ? 'not-allowed' : 'pointer', transition: 'all 0.15s' }}
                                        onMouseEnter={(e) => { if (!isBusy(s.id, 'pin') && !s.isPinned) { e.currentTarget.style.color = '#fbbf24'; e.currentTarget.style.background = 'rgba(251,191,36,0.15)'; } }}
                                        onMouseLeave={(e) => { if (!isBusy(s.id, 'pin') && !s.isPinned) { e.currentTarget.style.color = 'var(--text-muted)'; e.currentTarget.style.background = 'transparent'; } }}>
                                        {isBusy(s.id, 'pin') ? (
                                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="animate-spin"><path d="M21 12a9 9 0 1 1-6.219-8.56" /></svg>
                                        ) : (
                                            <svg width="14" height="14" viewBox="0 0 24 24" fill={s.isPinned ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                                <line x1="12" y1="17" x2="12" y2="22" />
                                                <path d="M5 17h14v-1.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V6h1a2 2 0 0 0 0-4H8a2 2 0 0 0 0 4h1v4.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V17z" />
                                            </svg>
                                        )}
                                    </button>
                                )}
                                {/* Rename — sadece aktif view'da */}
                                {!showArchived && (
                                    <button
                                        onClick={(e) => { e.stopPropagation(); if (renamingSessionId !== s.id) onStartRename(s); }}
                                        disabled={renamingSessionId === s.id}
                                        aria-label="Yeniden adlandır"
                                        title="Yeniden adlandır"
                                        style={{ width: '26px', height: '26px', display: 'flex', alignItems: 'center', justifyContent: 'center', borderRadius: '8px', color: renamingSessionId === s.id ? 'var(--accent-light)' : 'var(--text-muted)', background: 'transparent', border: 'none', cursor: renamingSessionId === s.id ? 'not-allowed' : 'pointer', transition: 'all 0.15s' }}
                                        onMouseEnter={(e) => { if (renamingSessionId !== s.id) { e.currentTarget.style.color = '#c4b5fd'; e.currentTarget.style.background = 'rgba(var(--accent-light-rgb),0.15)'; } }}
                                        onMouseLeave={(e) => { if (renamingSessionId !== s.id) { e.currentTarget.style.color = 'var(--text-muted)'; e.currentTarget.style.background = 'transparent'; } }}>
                                        {renamingSessionId === s.id ? (
                                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="animate-spin">
                                                <path d="M21 12a9 9 0 1 1-6.219-8.56" />
                                            </svg>
                                        ) : (
                                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                                <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
                                                <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
                                            </svg>
                                        )}
                                    </button>
                                )}
                                {/* Archive / Unarchive — view'a göre */}
                                {!showArchived ? (
                                    <button
                                        onClick={(e) => { e.stopPropagation(); if (!isBusy(s.id, 'archive')) onArchive?.(s.id); }}
                                        disabled={isBusy(s.id, 'archive')}
                                        aria-label="Arşive taşı"
                                        title="Arşive taşı"
                                        style={{ width: '26px', height: '26px', display: 'flex', alignItems: 'center', justifyContent: 'center', borderRadius: '8px', color: 'var(--text-muted)', background: 'transparent', border: 'none', cursor: isBusy(s.id, 'archive') ? 'not-allowed' : 'pointer', transition: 'all 0.15s' }}
                                        onMouseEnter={(e) => { if (!isBusy(s.id, 'archive')) { e.currentTarget.style.color = '#fb923c'; e.currentTarget.style.background = 'rgba(251,146,60,0.12)'; } }}
                                        onMouseLeave={(e) => { e.currentTarget.style.color = 'var(--text-muted)'; e.currentTarget.style.background = 'transparent'; }}>
                                        {isBusy(s.id, 'archive') ? (
                                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="animate-spin"><path d="M21 12a9 9 0 1 1-6.219-8.56" /></svg>
                                        ) : (
                                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                                <polyline points="21 8 21 21 3 21 3 8" /><rect x="1" y="3" width="22" height="5" /><line x1="10" y1="12" x2="14" y2="12" />
                                            </svg>
                                        )}
                                    </button>
                                ) : (
                                    <button
                                        onClick={(e) => { e.stopPropagation(); if (!isBusy(s.id, 'archive')) onUnarchive?.(s.id); }}
                                        disabled={isBusy(s.id, 'archive')}
                                        aria-label="Arşivden çıkar"
                                        title="Arşivden çıkar"
                                        style={{ width: '26px', height: '26px', display: 'flex', alignItems: 'center', justifyContent: 'center', borderRadius: '8px', color: 'var(--text-muted)', background: 'transparent', border: 'none', cursor: isBusy(s.id, 'archive') ? 'not-allowed' : 'pointer', transition: 'all 0.15s' }}
                                        onMouseEnter={(e) => { if (!isBusy(s.id, 'archive')) { e.currentTarget.style.color = '#86efac'; e.currentTarget.style.background = 'rgba(34,197,94,0.12)'; } }}
                                        onMouseLeave={(e) => { e.currentTarget.style.color = 'var(--text-muted)'; e.currentTarget.style.background = 'transparent'; }}>
                                        {isBusy(s.id, 'archive') ? (
                                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="animate-spin"><path d="M21 12a9 9 0 1 1-6.219-8.56" /></svg>
                                        ) : (
                                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                                <rect width="20" height="5" x="2" y="3" rx="1" /><path d="M4 8v11a2 2 0 0 0 2 2h2" /><path d="M20 8v11a2 2 0 0 1-2 2h-2" /><path d="m9 15 3-3 3 3" /><path d="M12 12v9" />
                                            </svg>
                                        )}
                                    </button>
                                )}
                                {/* Delete */}
                                <button onClick={(e) => { e.stopPropagation(); if (deletingSessionId !== s.id) onDeleteSession(s.id); }}
                                    disabled={deletingSessionId === s.id}
                                    aria-label="Sohbeti sil"
                                    title="Sil"
                                    style={{ width: '26px', height: '26px', display: 'flex', alignItems: 'center', justifyContent: 'center', borderRadius: '8px', color: 'var(--text-muted)', background: 'transparent', border: 'none', cursor: deletingSessionId === s.id ? 'not-allowed' : 'pointer', transition: 'all 0.15s' }}
                                    onMouseEnter={(e) => { if (deletingSessionId !== s.id) { e.currentTarget.style.color = '#fca5a5'; e.currentTarget.style.background = 'rgba(239,68,68,0.15)'; } }}
                                    onMouseLeave={(e) => { e.currentTarget.style.color = 'var(--text-muted)'; e.currentTarget.style.background = 'transparent'; }}>
                                    {deletingSessionId === s.id ? (
                                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="animate-spin">
                                            <path d="M21 12a9 9 0 1 1-6.219-8.56" />
                                        </svg>
                                    ) : (
                                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                            <polyline points="3 6 5 6 21 6" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
                                        </svg>
                                    )}
                                </button>
                            </div>
                        </div>
                        );
                    })}
                </div>

                {/* Alt butonlar */}
                <div style={{ padding: '12px', borderTop: '1px solid var(--border)', display: 'flex', flexDirection: 'column', gap: '4px' }}>
                    {/* Arşiv toggle */}
                    <button
                        onClick={onToggleArchived}
                        title={showArchived ? 'Aktif sohbetlere dön' : 'Arşivi göster'}
                        style={{
                            display: 'flex', alignItems: 'center', gap: '10px',
                            padding: '8px 12px', borderRadius: '8px',
                            background: showArchived ? 'rgba(251,146,60,0.15)' : 'transparent',
                            border: '1px solid ' + (showArchived ? 'rgba(251,146,60,0.35)' : 'transparent'),
                            color: showArchived ? '#fb923c' : 'var(--text-secondary)',
                            cursor: 'pointer', fontSize: '13px', fontWeight: 500, transition: 'all 0.15s'
                        }}
                        onMouseEnter={(e) => { if (!showArchived) { e.currentTarget.style.background = 'rgba(255,255,255,0.04)'; e.currentTarget.style.color = '#fb923c'; } }}
                        onMouseLeave={(e) => { if (!showArchived) { e.currentTarget.style.background = 'transparent'; e.currentTarget.style.color = 'var(--text-secondary)'; } }}>
                        {/* Arşivdeyken: geri-ok + "…Dön" (eylem); normalde: arşiv kutusu + "Arşiv" (hedef) */}
                        {showArchived ? (
                            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                <line x1="19" y1="12" x2="5" y2="12" /><polyline points="12 19 5 12 12 5" />
                            </svg>
                        ) : (
                            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                <polyline points="21 8 21 21 3 21 3 8" /><rect x="1" y="3" width="22" height="5" /><line x1="10" y1="12" x2="14" y2="12" />
                            </svg>
                        )}
                        <span style={{ flex: 1, textAlign: 'left' }}>{showArchived ? 'Aktif Sohbetlere Dön' : 'Arşiv'}</span>
                        {!showArchived && archivedCount > 0 && (
                            <span style={{ fontSize: '11px', padding: '2px 7px', borderRadius: '10px', background: 'rgba(251,146,60,0.2)', color: '#fb923c', fontWeight: 600 }}>
                                {archivedCount}
                            </span>
                        )}
                    </button>
                    <SidebarButton onClick={() => navigate('/profile')} label="Profil"
                        icon={<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" /><circle cx="12" cy="7" r="4" /></svg>}
                    />
                    {/* Yönetici de belge yönetimi için /admin'e gider (içeride yalnız Belgeler sekmesini görür).
                        Sadece Admin'e gösterilirse yönetici chat'e düştüğünde geri dönemez. */}
                    {(user?.roles?.includes('Admin') || user?.roles?.includes('Manager')) && (
                        <SidebarButton onClick={() => navigate('/admin')} label="Yönetim"
                            icon={<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><rect x="3" y="3" width="7" height="7" /><rect x="14" y="3" width="7" height="7" /><rect x="14" y="14" width="7" height="7" /><rect x="3" y="14" width="7" height="7" /></svg>}
                        />
                    )}
                    <SidebarButton onClick={onLogout} label="Çıkış Yap"
                        icon={<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" /><polyline points="16 17 21 12 16 7" /><line x1="21" y1="12" x2="9" y2="12" /></svg>}
                    />
                </div>
            </div>
        </>
    );
}