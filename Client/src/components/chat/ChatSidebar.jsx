import { useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { SessionSkeleton } from '../shared/Skeleton';
import SidebarButton from './SidebarButton';

export default function ChatSidebar({
    sessions, sessionsLoading, activeSession,
    editingSessionId, editingTitle,
    collapsed, onToggleCollapse,
    onNewChat, onLoadSession,
    onStartRename, onCommitRename, onSetEditingTitle, onSetEditingSessionId,
    onDeleteSession, user, onLogout,
}) {
    const renameInputRef = useRef(null);
    const navigate = useNavigate();

    if (collapsed) {
        return (
            <div style={{ width: '56px', background: 'var(--surface)', borderRight: '1px solid var(--border)', display: 'flex', flexDirection: 'column', alignItems: 'center', padding: '16px 0', gap: '12px', flexShrink: 0 }}>
                <div style={{ width: '32px', height: '32px', borderRadius: '8px', display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'var(--accent)', marginBottom: '4px' }}>
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
                        <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                    </svg>
                </div>
                <button onClick={onToggleCollapse} title="Paneli Genişlet"
                    style={{ color: '#64748b', background: 'none', border: 'none', cursor: 'pointer', padding: '6px' }}>
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                        <line x1="3" y1="12" x2="21" y2="12" /><line x1="3" y1="6" x2="21" y2="6" /><line x1="3" y1="18" x2="21" y2="18" />
                    </svg>
                </button>
                <button onClick={onNewChat} title="Yeni Sohbet"
                    style={{ color: '#94a3b8', background: 'none', border: 'none', cursor: 'pointer', padding: '6px' }}>
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
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

            <div className="chat-sidebar" style={{ width: '256px', background: 'var(--surface)', borderRight: '1px solid var(--border)', display: 'flex', flexDirection: 'column', flexShrink: 0, transition: 'width 0.2s' }}>
                {/* Logo + Daralt */}
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '16px', borderBottom: '1px solid var(--border)' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                        <div style={{ width: '32px', height: '32px', borderRadius: '8px', display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'var(--accent)', flexShrink: 0 }}>
                            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
                                <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                            </svg>
                        </div>
                        <span style={{ fontWeight: 700, color: 'white', fontSize: '15px' }}>DocuChat</span>
                    </div>
                    <button onClick={onToggleCollapse} title="Paneli Daralt"
                        style={{ color: '#64748b', background: 'none', border: 'none', cursor: 'pointer', padding: '4px' }}
                        onMouseEnter={(e) => e.currentTarget.style.color = '#94a3b8'}
                        onMouseLeave={(e) => e.currentTarget.style.color = '#64748b'}>
                        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                            <line x1="3" y1="12" x2="21" y2="12" /><line x1="3" y1="6" x2="21" y2="6" /><line x1="3" y1="18" x2="21" y2="18" />
                        </svg>
                    </button>
                </div>

                {/* Yeni Sohbet */}
                <div style={{ padding: '12px' }}>
                    <button onClick={onNewChat} style={{ width: '100%', display: 'flex', alignItems: 'center', gap: '8px', padding: '10px 12px', borderRadius: '10px', fontSize: '14px', fontWeight: 500, color: 'white', background: 'var(--accent)', border: 'none', cursor: 'pointer' }}>
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                            <line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" />
                        </svg>
                        Yeni Sohbet
                    </button>
                </div>

                {/* Session listesi */}
                <div style={{ flex: 1, overflowY: 'auto', padding: '0 12px 12px', display: 'flex', flexDirection: 'column', gap: '2px' }}>
                    {sessionsLoading ? <SessionSkeleton /> : sessions.length === 0 ? (
                        <p style={{ fontSize: '12px', textAlign: 'center', marginTop: '16px', color: 'var(--gray-light)' }}>Henüz sohbet yok</p>
                    ) : null}
                    {sessions.map((s) => (
                        <div key={s.id} onClick={() => onLoadSession(s)}
                            className="group"
                            style={{
                                display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                                padding: '8px 10px', borderRadius: '10px', cursor: 'pointer', transition: 'background 0.15s',
                                background: activeSession?.id === s.id ? 'var(--navy-light)' : 'transparent',
                                border: activeSession?.id === s.id ? '1px solid var(--border)' : '1px solid transparent',
                            }}
                            onMouseEnter={(e) => { if (activeSession?.id !== s.id) e.currentTarget.style.background = 'var(--surface2)'; }}
                            onMouseLeave={(e) => { if (activeSession?.id !== s.id) e.currentTarget.style.background = 'transparent'; }}>
                            {editingSessionId === s.id ? (
                                <input ref={renameInputRef} value={editingTitle}
                                    onChange={(e) => onSetEditingTitle(e.target.value)}
                                    onBlur={() => onCommitRename(s.id)}
                                    onKeyDown={(e) => { if (e.key === 'Enter') onCommitRename(s.id); if (e.key === 'Escape') onSetEditingSessionId(null); }}
                                    onClick={(e) => e.stopPropagation()}
                                    style={{ flex: 1, fontSize: '14px', background: 'transparent', outline: 'none', border: 'none', borderBottom: '1px solid var(--accent)', color: 'white' }}
                                    autoFocus
                                />
                            ) : (
                                <span style={{ fontSize: '13px', flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', color: activeSession?.id === s.id ? '#e2e8f0' : '#94a3b8', maxWidth: '150px' }}>
                                    {s.title || 'Sohbet'}
                                </span>
                            )}
                            <div style={{ display: 'flex', alignItems: 'center', gap: '2px', flexShrink: 0, marginLeft: '4px', opacity: 0 }}
                                onMouseEnter={(e) => e.currentTarget.style.opacity = '1'}
                                className="session-actions">
                                <button onClick={(e) => { e.stopPropagation(); onStartRename(s); }}
                                    style={{ padding: '4px', borderRadius: '4px', color: '#64748b', background: 'none', border: 'none', cursor: 'pointer' }}
                                    onMouseEnter={(e) => e.currentTarget.style.color = '#93c5fd'}
                                    onMouseLeave={(e) => e.currentTarget.style.color = '#64748b'}>
                                    <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                        <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
                                        <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
                                    </svg>
                                </button>
                                <button onClick={(e) => { e.stopPropagation(); onDeleteSession(s.id); }}
                                    style={{ padding: '4px', borderRadius: '4px', color: '#64748b', background: 'none', border: 'none', cursor: 'pointer' }}
                                    onMouseEnter={(e) => e.currentTarget.style.color = '#ef4444'}
                                    onMouseLeave={(e) => e.currentTarget.style.color = '#64748b'}>
                                    <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                        <polyline points="3 6 5 6 21 6" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
                                    </svg>
                                </button>
                            </div>
                        </div>
                    ))}
                </div>

                {/* Alt butonlar */}
                <div style={{ padding: '12px', borderTop: '1px solid var(--border)', display: 'flex', flexDirection: 'column', gap: '4px' }}>
                    {user?.roles?.includes('Admin') && (
                        <SidebarButton onClick={() => navigate('/admin')} label="Admin Panel"
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