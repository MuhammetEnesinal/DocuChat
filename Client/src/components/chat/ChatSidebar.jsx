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
            <div className="flex flex-col items-center py-4 gap-3 flex-shrink-0"
                style={{ width: '56px', background: 'var(--surface)', borderRight: '1px solid var(--border)' }}>
                {/* Logo */}
                <div className="w-8 h-8 rounded-lg flex items-center justify-center flex-shrink-0 mb-1"
                    style={{ background: 'var(--accent)' }}>
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
                        <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                    </svg>
                </div>
                {/* Genişlet butonu */}
                <button onClick={onToggleCollapse}
                    style={{ color: '#64748b', background: 'none', border: 'none', cursor: 'pointer', padding: '6px' }}
                    title="Paneli Genişlet">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                        <line x1="3" y1="12" x2="21" y2="12" /><line x1="3" y1="6" x2="21" y2="6" /><line x1="3" y1="18" x2="21" y2="18" />
                    </svg>
                </button>
                {/* Yeni sohbet */}
                <button onClick={onNewChat}
                    style={{ color: '#94a3b8', background: 'none', border: 'none', cursor: 'pointer', padding: '6px' }}
                    title="Yeni Sohbet">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                        <line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" />
                    </svg>
                </button>
            </div>
        );
    }

    return (
        <div className="flex flex-col flex-shrink-0 transition-all"
            style={{ width: '256px', background: 'var(--surface)', borderRight: '1px solid var(--border)' }}>
            {/* Logo + Daralt */}
            <div className="flex items-center justify-between px-4 py-4"
                style={{ borderBottom: '1px solid var(--border)' }}>
                <div className="flex items-center gap-3">
                    <div className="w-8 h-8 rounded-lg flex items-center justify-center flex-shrink-0"
                        style={{ background: 'var(--accent)' }}>
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
                            <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                        </svg>
                    </div>
                    <span className="font-bold text-white text-base">DocuChat</span>
                </div>
                <button onClick={onToggleCollapse}
                    style={{ color: '#64748b', background: 'none', border: 'none', cursor: 'pointer', padding: '4px' }}
                    title="Paneli Daralt"
                    onMouseEnter={(e) => e.currentTarget.style.color = '#94a3b8'}
                    onMouseLeave={(e) => e.currentTarget.style.color = '#64748b'}>
                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                        <line x1="3" y1="12" x2="21" y2="12" /><line x1="3" y1="6" x2="21" y2="6" /><line x1="3" y1="18" x2="21" y2="18" />
                    </svg>
                </button>
            </div>

            {/* Yeni Sohbet */}
            <div className="px-3 pt-3">
                <button onClick={onNewChat}
                    className="w-full flex items-center gap-2 px-3 py-2.5 rounded-xl text-sm font-medium transition-all"
                    style={{ background: 'var(--accent)', color: 'white' }}>
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                        <line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" />
                    </svg>
                    Yeni Sohbet
                </button>
            </div>

            {/* Session listesi */}
            <div className="flex-1 overflow-y-auto px-3 py-3 space-y-1">
                {sessionsLoading ? <SessionSkeleton /> : sessions.length === 0 && (
                    <p className="text-xs text-center mt-4" style={{ color: 'var(--gray-light)' }}>Henüz sohbet yok</p>
                )}
                {sessions.map((s) => (
                    <div key={s.id} onClick={() => onLoadSession(s)}
                        className="group flex items-center justify-between px-3 py-2.5 rounded-xl cursor-pointer transition-all"
                        style={{
                            background: activeSession?.id === s.id ? 'var(--navy-light)' : 'transparent',
                            border: activeSession?.id === s.id ? '1px solid var(--border)' : '1px solid transparent',
                        }}>
                        {editingSessionId === s.id ? (
                            <input
                                ref={renameInputRef}
                                value={editingTitle}
                                onChange={(e) => onSetEditingTitle(e.target.value)}
                                onBlur={() => onCommitRename(s.id)}
                                onKeyDown={(e) => {
                                    if (e.key === 'Enter') onCommitRename(s.id);
                                    if (e.key === 'Escape') onSetEditingSessionId(null);
                                }}
                                onClick={(e) => e.stopPropagation()}
                                className="flex-1 text-sm bg-transparent outline-none border-b text-white"
                                style={{ borderColor: 'var(--accent)' }}
                                autoFocus
                            />
                        ) : (
                            <span className="text-sm truncate flex-1"
                                style={{ color: activeSession?.id === s.id ? '#e2e8f0' : '#94a3b8', maxWidth: '140px' }}>
                                {s.title || 'Sohbet'}
                            </span>
                        )}
                        <div className="opacity-0 group-hover:opacity-100 flex items-center gap-1 flex-shrink-0 ml-1">
                            <button onClick={(e) => { e.stopPropagation(); onStartRename(s); }}
                                className="p-1 rounded" style={{ color: '#64748b', background: 'none', border: 'none', cursor: 'pointer' }}
                                onMouseEnter={(e) => e.currentTarget.style.color = '#93c5fd'}
                                onMouseLeave={(e) => e.currentTarget.style.color = '#64748b'}>
                                <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
                                    <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
                                </svg>
                            </button>
                            <button onClick={(e) => { e.stopPropagation(); onDeleteSession(s.id); }}
                                className="p-1 rounded" style={{ color: '#64748b', background: 'none', border: 'none', cursor: 'pointer' }}
                                onMouseEnter={(e) => e.currentTarget.style.color = '#ef4444'}
                                onMouseLeave={(e) => e.currentTarget.style.color = '#64748b'}>
                                <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                    <polyline points="3 6 5 6 21 6" />
                                    <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
                                </svg>
                            </button>
                        </div>
                    </div>
                ))}
            </div>

            {/* Alt butonlar */}
            <div className="px-3 pb-3 pt-3 space-y-1" style={{ borderTop: '1px solid var(--border)' }}>
                {user?.roles?.includes('Admin') && (
                    <SidebarButton
                        onClick={() => navigate('/admin')}
                        label="Admin Panel"
                        icon={
                            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                <rect x="3" y="3" width="7" height="7" /><rect x="14" y="3" width="7" height="7" />
                                <rect x="14" y="14" width="7" height="7" /><rect x="3" y="14" width="7" height="7" />
                            </svg>
                        }
                    />
                )}
                <SidebarButton
                    onClick={onLogout}
                    label="Çıkış Yap"
                    icon={
                        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                            <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
                            <polyline points="16 17 21 12 16 7" /><line x1="21" y1="12" x2="9" y2="12" />
                        </svg>
                    }
                />
            </div>
        </div>
    );
}