import { useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { SessionSkeleton } from '../shared/Skeleton';
import SidebarButton from './SidebarButton';

export default function ChatSidebar({
    sessions, sessionsLoading, activeSession,
    editingSessionId, editingTitle, deletingSessionId, renamingSessionId,
    collapsed, onToggleCollapse,
    onNewChat, onLoadSession,
    onStartRename, onCommitRename, onSetEditingTitle, onSetEditingSessionId,
    onDeleteSession, user, onLogout,
}) {
    const renameInputRef = useRef(null);
    const navigate = useNavigate();

    if (collapsed) {
        return (
            <div style={{ width: '60px', background: 'rgba(50, 45, 90, 0.32)', backdropFilter: 'blur(40px) saturate(180%)', WebkitBackdropFilter: 'blur(40px) saturate(180%)', borderRight: '1px solid rgba(167,139,250,0.18)', display: 'flex', flexDirection: 'column', alignItems: 'center', padding: '16px 0', gap: '14px', flexShrink: 0 }}>
                <div style={{ width: '36px', height: '36px', borderRadius: '10px', display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'var(--gradient-accent)', marginBottom: '4px', boxShadow: '0 6px 20px -6px rgba(99,102,241,0.6)' }}>
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
                        <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                    </svg>
                </div>
                <button onClick={onToggleCollapse} title="Paneli Genişlet"
                    style={{ color: 'var(--text-muted)', background: 'none', border: 'none', cursor: 'pointer', padding: '6px' }}>
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                        <line x1="3" y1="12" x2="21" y2="12" /><line x1="3" y1="6" x2="21" y2="6" /><line x1="3" y1="18" x2="21" y2="18" />
                    </svg>
                </button>
                <button onClick={onNewChat} title="Yeni Sohbet"
                    style={{ color: 'var(--text-muted)', background: 'none', border: 'none', cursor: 'pointer', padding: '6px' }}>
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

            <div className="chat-sidebar" style={{ width: '280px', background: 'rgba(50, 45, 90, 0.32)', backdropFilter: 'blur(40px) saturate(180%)', WebkitBackdropFilter: 'blur(40px) saturate(180%)', borderRight: '1px solid rgba(167,139,250,0.18)', display: 'flex', flexDirection: 'column', flexShrink: 0, transition: 'width 0.2s', boxShadow: 'inset -1px 0 0 rgba(255,255,255,0.04), 4px 0 30px -10px rgba(0,0,0,0.4)' }}>
                {/* Logo + Daralt */}
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '20px 18px', borderBottom: '1px solid var(--glass-border)' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                        <div style={{ width: '34px', height: '34px', borderRadius: '10px', display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'var(--gradient-accent)', flexShrink: 0, boxShadow: '0 6px 18px -6px rgba(99,102,241,0.55)' }}>
                            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
                                <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                            </svg>
                        </div>
                        <span className="gradient-text" style={{ fontWeight: 700, fontSize: '16px', letterSpacing: '-0.01em' }}>DocuChat</span>
                    </div>
                    <button onClick={onToggleCollapse} title="Paneli Daralt"
                        style={{ color: 'var(--text-muted)', background: 'none', border: 'none', cursor: 'pointer', padding: '4px' }}
                        onMouseEnter={(e) => e.currentTarget.style.color = 'var(--text-muted)'}
                        onMouseLeave={(e) => e.currentTarget.style.color = 'var(--text-muted)'}>
                        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
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

                {/* Session listesi */}
                <div role="list" style={{ flex: 1, overflowY: 'auto', padding: '0 10px 12px', display: 'flex', flexDirection: 'column', gap: '3px' }}>
                    {sessionsLoading ? <SessionSkeleton /> : sessions.length === 0 ? (
                        <p style={{ fontSize: '12px', textAlign: 'center', marginTop: '16px', color: 'var(--gray-light)' }}>Henüz sohbet yok</p>
                    ) : null}
                    {sessions.map((s) => (
                        <div key={s.id}
                            role="listitem"
                            tabIndex={0}
                            aria-label={s.title || 'Sohbet'}
                            aria-current={activeSession?.id === s.id ? 'true' : undefined}
                            onClick={() => onLoadSession(s)}
                            onKeyDown={(e) => {
                                if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onLoadSession(s); }
                                else if (e.key === 'Delete' || e.key === 'Backspace') { e.preventDefault(); onDeleteSession(s.id); }
                                else if (e.key === 'F2') { e.preventDefault(); onStartRename(s); }
                            }}
                            className="group"
                            style={{
                                display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                                padding: '11px 12px', borderRadius: '12px', cursor: 'pointer', transition: 'all 0.15s',
                                background: activeSession?.id === s.id ? 'linear-gradient(135deg, rgba(139,92,246,0.22) 0%, rgba(99,102,241,0.16) 100%)' : 'transparent',
                                border: activeSession?.id === s.id ? '1px solid rgba(167,139,250,0.35)' : '1px solid transparent',
                                boxShadow: activeSession?.id === s.id ? '0 4px 14px -6px rgba(139,92,246,0.4), inset 0 1px 0 rgba(255,255,255,0.05)' : 'none',
                                outline: 'none',
                            }}
                            onFocus={(e) => { if (activeSession?.id !== s.id) e.currentTarget.style.background = 'rgba(255,255,255,0.04)'; }}
                            onBlur={(e) => { if (activeSession?.id !== s.id) e.currentTarget.style.background = 'transparent'; }}
                            onMouseEnter={(e) => { if (activeSession?.id !== s.id) e.currentTarget.style.background = 'rgba(255,255,255,0.04)'; }}
                            onMouseLeave={(e) => { if (activeSession?.id !== s.id) e.currentTarget.style.background = 'transparent'; }}>
                            {editingSessionId === s.id ? (
                                <input ref={renameInputRef} value={editingTitle}
                                    onChange={(e) => onSetEditingTitle(e.target.value)}
                                    onBlur={() => onCommitRename(s.id)}
                                    onKeyDown={(e) => { if (e.key === 'Enter') onCommitRename(s.id); if (e.key === 'Escape') onSetEditingSessionId(null); }}
                                    onClick={(e) => e.stopPropagation()}
                                    maxLength={60}
                                    style={{ flex: 1, fontSize: '14px', background: 'transparent', outline: 'none', border: 'none', borderBottom: '1px solid #a78bfa', color: '#fff' }}
                                    autoFocus
                                />
                            ) : (
                                <span style={{ fontSize: '13.5px', fontWeight: activeSession?.id === s.id ? 600 : 500, flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', color: activeSession?.id === s.id ? '#fff' : 'rgba(255,255,255,0.7)' }}>
                                    {s.title || 'Sohbet'}
                                </span>
                            )}
                            <div style={{ display: 'flex', alignItems: 'center', gap: '4px', flexShrink: 0, marginLeft: '6px' }}
                                className="session-actions">
                                <button
                                    onClick={(e) => { e.stopPropagation(); if (renamingSessionId !== s.id) onStartRename(s); }}
                                    disabled={renamingSessionId === s.id}
                                    aria-label="Yeniden adlandır"
                                    style={{ width: '26px', height: '26px', display: 'flex', alignItems: 'center', justifyContent: 'center', borderRadius: '8px', color: renamingSessionId === s.id ? '#a78bfa' : 'rgba(255,255,255,0.5)', background: 'transparent', border: 'none', cursor: renamingSessionId === s.id ? 'not-allowed' : 'pointer', transition: 'all 0.15s' }}
                                    onMouseEnter={(e) => { if (renamingSessionId !== s.id) { e.currentTarget.style.color = '#c4b5fd'; e.currentTarget.style.background = 'rgba(167,139,250,0.15)'; } }}
                                    onMouseLeave={(e) => { if (renamingSessionId !== s.id) { e.currentTarget.style.color = 'rgba(255,255,255,0.5)'; e.currentTarget.style.background = 'transparent'; } }}>
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
                                <button onClick={(e) => { e.stopPropagation(); if (deletingSessionId !== s.id) onDeleteSession(s.id); }}
                                    disabled={deletingSessionId === s.id}
                                    aria-label="Sohbeti sil"
                                    style={{ width: '26px', height: '26px', display: 'flex', alignItems: 'center', justifyContent: 'center', borderRadius: '8px', color: 'rgba(255,255,255,0.5)', background: 'transparent', border: 'none', cursor: deletingSessionId === s.id ? 'not-allowed' : 'pointer', transition: 'all 0.15s' }}
                                    onMouseEnter={(e) => { if (deletingSessionId !== s.id) { e.currentTarget.style.color = '#fca5a5'; e.currentTarget.style.background = 'rgba(239,68,68,0.15)'; } }}
                                    onMouseLeave={(e) => { e.currentTarget.style.color = 'rgba(255,255,255,0.5)'; e.currentTarget.style.background = 'transparent'; }}>
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
                    ))}
                </div>

                {/* Alt butonlar */}
                <div style={{ padding: '12px', borderTop: '1px solid var(--border)', display: 'flex', flexDirection: 'column', gap: '4px' }}>
                    <SidebarButton onClick={() => navigate('/profile')} label="Profil"
                        icon={<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" /><circle cx="12" cy="7" r="4" /></svg>}
                    />
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