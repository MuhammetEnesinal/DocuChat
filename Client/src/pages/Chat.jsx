import { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { Virtuoso } from 'react-virtuoso';
import { getPopularQuestions } from '../services/api';
import { formatDateShort } from '../utils/format';
import { useToast } from '../components/shared/Toast';
import { useAuth } from '../hooks/useAuth';
import { useSessions } from '../hooks/useSessions';
import { useChatMessages } from '../hooks/useChatMessages';
import ChatSidebar from '../components/chat/ChatSidebar';
import MessageBubble from '../components/chat/MessageBubble';
import SourcePanel from '../components/chat/SourcePanel';
import EmptyState from '../components/chat/EmptyState';
import ChatInput from '../components/chat/ChatInput';
import ThemeToggle from '../components/shared/ThemeToggle';
import TypingIndicator from '../components/chat/TypingIndicator';
import { MessageSkeleton } from '../components/shared/Skeleton';
import ConfirmDialog from '../components/shared/ConfirmDialog';

function buildVirtualItems(messages) {
    const items = [];
    let lastDate = null;
    for (const msg of messages) {
        const date = formatDateShort(msg.createdAt);
        if (date !== lastDate) {
            items.push({ type: 'separator', date, key: `sep_${date}` });
            lastDate = date;
        }
        items.push({ type: 'message', msg, key: msg.id });
    }
    return items;
}

export default function Chat() {
    const [popularQuestions, setPopularQuestions] = useState([]);
    const [popularQuestionsLoading, setPopularQuestionsLoading] = useState(true);
    const [sidebarCollapsed, setSidebarCollapsed] = useState(window.innerWidth < 768);
    const [question, setQuestion] = useState('');
    const [confirmSession, setConfirmSession] = useState(null);

    const virtuosoRef = useRef(null);
    const { user, logout } = useAuth();
    const navigate = useNavigate();
    const toast = useToast();

    const {
        sessions, setSessions,
        activeSession, setActiveSession,
        sessionsLoading,
        editingSessionId, setEditingSessionId,
        editingTitle, setEditingTitle,
        deletingSessionId,
        renamingSessionId,
        fetchSessions,
        handleDeleteSession,
        handleStartRename,
        handleCommitRename,
    } = useSessions();

    const {
        messages,
        loading,
        messagesLoading,
        hasMoreMessages,
        loadingMore,
        chunks, setChunks,
        showChunks, setShowChunks,
        copiedId,
        clearMessages,
        loadMessages,
        loadMoreMessages,
        handleSend,
        handleAbort,
        handleCopy,
    } = useChatMessages(virtuosoRef);

    useEffect(() => {
        fetchSessions();
        (async () => {
            setPopularQuestionsLoading(true);
            try {
                const res = await getPopularQuestions(6);
                setPopularQuestions(res.data.data || []);
            } catch { /* popular questions non-critical */ }
            finally { setPopularQuestionsLoading(false); }
        })();
    }, []);

    useEffect(() => {
        const handleResize = () => { if (window.innerWidth < 768) setSidebarCollapsed(true); };
        window.addEventListener('resize', handleResize);
        return () => window.removeEventListener('resize', handleResize);
    }, []);

    const loadSession = async (session) => {
        setActiveSession(session);
        clearMessages();
        if (window.innerWidth < 768) setSidebarCollapsed(true);
        await loadMessages(session);
    };

    const newChat = () => {
        setActiveSession(null);
        clearMessages();
        if (window.innerWidth < 768) setSidebarCollapsed(true);
    };

    const onSend = async (forcedQuestion, skipClarification = false) => {
        const q = (forcedQuestion ?? question).trim();
        if (!q || loading) return;
        if (!forcedQuestion) setQuestion('');
        await handleSend(q, activeSession, (newSession) => {
            setActiveSession(newSession);
            setSessions(prev => [newSession, ...prev]);
        }, skipClarification);
    };

    const onClarificationSelect = (opt) => onSend(opt, true);

    const onClarificationDismiss = (clarificationMsgId) => {
        setMessages(prev => prev.filter(m => m.id !== clarificationMsgId));
    };

    const onDeleteSession = (sessionId) => {
        setConfirmSession(sessionId);
    };

    const confirmDeleteSession = async () => {
        const sessionId = confirmSession;
        setConfirmSession(null);
        await handleDeleteSession(sessionId, (id) => {
            if (activeSession?.id === id) newChat();
        });
    };

    const virtualItems = buildVirtualItems(messages);

    const renderVirtualItem = (index) => {
        const item = virtualItems[index];
        if (!item) return null;

        if (item.type === 'separator') {
            return (
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px', margin: '8px 0' }}>
                    <div style={{ flex: 1, height: '1px', background: 'var(--border)' }} />
                    <span style={{ fontSize: '12px', color: 'var(--gray-light)', padding: '0 8px' }}>{item.date}</span>
                    <div style={{ flex: 1, height: '1px', background: 'var(--border)' }} />
                </div>
            );
        }

        return (
            <div style={{ padding: '0 0 24px 0' }}>
                <MessageBubble msg={item.msg} copiedId={copiedId} onCopy={handleCopy} onRetry={onSend} onClarificationSelect={onClarificationSelect} onClarificationDismiss={onClarificationDismiss} />
            </div>
        );
    };

    return (
        <div style={{ display: 'flex', height: '100dvh', background: 'var(--navy)' }}>
            <ChatSidebar
                sessions={sessions}
                sessionsLoading={sessionsLoading}
                activeSession={activeSession}
                editingSessionId={editingSessionId}
                editingTitle={editingTitle}
                deletingSessionId={deletingSessionId}
                renamingSessionId={renamingSessionId}
                collapsed={sidebarCollapsed}
                onToggleCollapse={() => setSidebarCollapsed(!sidebarCollapsed)}
                onNewChat={newChat}
                onLoadSession={loadSession}
                onStartRename={handleStartRename}
                onCommitRename={handleCommitRename}
                onSetEditingTitle={setEditingTitle}
                onSetEditingSessionId={setEditingSessionId}
                onDeleteSession={onDeleteSession}
                user={user}
                onLogout={() => { logout(); navigate('/login'); }}
            />

            <div style={{ display: 'flex', flexDirection: 'column', flex: 1, minWidth: 0 }}>
                <div className="chat-header" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '16px 24px', borderBottom: '1px solid var(--border)', background: 'var(--surface)' }}>
                    <div style={{ minWidth: 0 }}>
                        <h1 style={{ fontSize: '15px', fontWeight: 600, color: 'var(--text-primary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', margin: 0 }}>
                            {activeSession?.title || 'Yeni Sohbet'}
                        </h1>
                        <p style={{ fontSize: '12px', marginTop: '2px', color: 'var(--gray-light)', margin: '2px 0 0' }}>
                            Tüm belgeler üzerinde arama yapılıyor
                        </p>
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexShrink: 0, marginLeft: '12px' }}>
                        {chunks.length > 0 && (
                            <button onClick={() => setShowChunks(!showChunks)}
                                style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '6px 12px', borderRadius: '8px', fontSize: '13px', fontWeight: 500, cursor: 'pointer', background: showChunks ? 'var(--accent)' : 'var(--surface2)', color: showChunks ? 'white' : '#94a3b8', border: '1px solid var(--border)' }}>
                                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" />
                                </svg>
                                Kaynaklar ({chunks.length})
                            </button>
                        )}
                        <ThemeToggle />
                    </div>
                </div>

                <div style={{ display: 'flex', flex: 1, minHeight: 0 }}>
                    <div style={{ display: 'flex', flexDirection: 'column', flex: 1, minWidth: 0 }}>
                        <div style={{ flex: 1, minHeight: 0 }}>
                            {messagesLoading ? (
                                <div style={{ padding: '24px' }}>
                                    <MessageSkeleton />
                                </div>
                            ) : messages.length === 0 ? (
                                <div style={{ height: '100%', overflowY: 'auto', padding: '24px' }}>
                                    <EmptyState popularQuestions={popularQuestions} popularQuestionsLoading={popularQuestionsLoading} onSelectQuestion={setQuestion} />
                                </div>
                            ) : (
                                <Virtuoso
                                    ref={virtuosoRef}
                                    style={{ height: '100%' }}
                                    totalCount={virtualItems.length + (loading ? 1 : 0)}
                                    initialTopMostItemIndex={virtualItems.length - 1}
                                    followOutput="smooth"
                                    startReached={hasMoreMessages ? () => loadMoreMessages(activeSession?.id) : undefined}
                                    components={{
                                        Header: hasMoreMessages ? () => (
                                            <div style={{ display: 'flex', justifyContent: 'center', padding: '8px 0' }}>
                                                <button
                                                    onClick={() => loadMoreMessages(activeSession?.id)}
                                                    disabled={loadingMore}
                                                    style={{
                                                        padding: '6px 16px', fontSize: '13px', borderRadius: '20px',
                                                        background: 'var(--surface2)', color: 'var(--text-muted)',
                                                        border: '1px solid var(--border)', cursor: loadingMore ? 'not-allowed' : 'pointer',
                                                        opacity: loadingMore ? 0.6 : 1,
                                                    }}>
                                                    {loadingMore ? 'Yükleniyor...' : 'Daha eski mesajları göster'}
                                                </button>
                                            </div>
                                        ) : undefined,
                                    }}
                                    itemContent={(index) => {
                                        if (loading && index === virtualItems.length) {
                                            return <div style={{ padding: '0 24px 24px' }}><TypingIndicator /></div>;
                                        }
                                        return (
                                            <div style={{ padding: '0 24px' }}>
                                                {renderVirtualItem(index)}
                                            </div>
                                        );
                                    }}
                                />
                            )}
                        </div>

                        <ChatInput
                            value={question}
                            onChange={setQuestion}
                            onSend={onSend}
                            loading={loading}
                            onAbort={handleAbort}
                        />
                    </div>

                    {showChunks && chunks.length > 0 && <SourcePanel chunks={chunks} />}
                </div>
            </div>

            {confirmSession && (
                <ConfirmDialog
                    title="Sohbeti Sil"
                    message="Bu sohbet kalıcı olarak silinecek. Emin misiniz?"
                    confirmLabel="Sil"
                    onConfirm={confirmDeleteSession}
                    onCancel={() => setConfirmSession(null)}
                />
            )}
        </div>
    );
}
