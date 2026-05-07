import { useState, useEffect, useRef, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import axios from 'axios';
import { Virtuoso } from 'react-virtuoso';
import { askQuestion, getSessions, getMessages, deleteSession, renameSession, getPopularQuestions } from '../services/api';
import { formatDateShort, getRateLimitMessage } from '../utils/format';
import { useToast } from '../components/shared/Toast';
import { useAuth } from '../hooks/useAuth';
import ChatSidebar from '../components/chat/ChatSidebar';
import MessageBubble from '../components/chat/MessageBubble';
import SourcePanel from '../components/chat/SourcePanel';
import EmptyState from '../components/chat/EmptyState';
import ChatInput from '../components/chat/ChatInput';
import ThemeToggle from '../components/shared/ThemeToggle';
import TypingIndicator from '../components/chat/TypingIndicator';
import { MessageSkeleton } from '../components/shared/Skeleton';

let _msgCounter = 0;
const nextMsgId = () => `msg_${Date.now()}_${++_msgCounter}`;

const PAGE_SIZE = 50;

// Mesaj listesini tarih gruplarına dönüştürür; Virtuoso için düz dizi üretir
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
    const [sessions, setSessions] = useState([]);
    const [activeSession, setActiveSession] = useState(null);
    const [messages, setMessages] = useState([]);
    const [question, setQuestion] = useState('');
    const [loading, setLoading] = useState(false);
    const [chunks, setChunks] = useState([]);
    const [showChunks, setShowChunks] = useState(false);
    const [copiedId, setCopiedId] = useState(null);
    const [editingSessionId, setEditingSessionId] = useState(null);
    const [editingTitle, setEditingTitle] = useState('');
    const [sessionsLoading, setSessionsLoading] = useState(true);
    const [popularQuestions, setPopularQuestions] = useState([]);
    const [popularQuestionsLoading, setPopularQuestionsLoading] = useState(true);
    const [deletingSessionId, setDeletingSessionId] = useState(null);
    const [renamingSessionId, setRenamingSessionId] = useState(null);
    const [sidebarCollapsed, setSidebarCollapsed] = useState(window.innerWidth < 768);
    const [messagesLoading, setMessagesLoading] = useState(false);
    const [hasMoreMessages, setHasMoreMessages] = useState(false);
    const [messagesPage, setMessagesPage] = useState(1);
    const [loadingMore, setLoadingMore] = useState(false);

    const virtuosoRef = useRef(null);
    const abortRef = useRef(null);
    const { user, logout } = useAuth();
    const navigate = useNavigate();
    const toast = useToast();

    useEffect(() => { fetchSessions(); fetchPopularQuestions(); }, []);

    const fetchPopularQuestions = async () => {
        setPopularQuestionsLoading(true);
        try {
            const res = await getPopularQuestions(6);
            setPopularQuestions(res.data.data || []);
        } catch { /* popular questions non-critical */ }
        finally { setPopularQuestionsLoading(false); }
    };

    const fetchSessions = async () => {
        setSessionsLoading(true);
        try {
            const res = await getSessions();
            setSessions(res.data.data || []);
        } catch { toast.error('Sohbet listesi yüklenemedi.'); }
        finally { setSessionsLoading(false); }
    };

    const parseMessages = (raw) =>
        (raw || []).map(m => ({
            ...m,
            images: m.imagesJson ? (() => {
                try { return JSON.parse(m.imagesJson); }
                catch (e) { console.warn('Resim JSON parse hatası:', e); return []; }
            })() : undefined,
        }));

    const loadSession = async (session) => {
        setActiveSession(session);
        setChunks([]);
        setShowChunks(false);
        setMessages([]);
        setHasMoreMessages(false);
        setMessagesPage(1);
        if (window.innerWidth < 768) setSidebarCollapsed(true);

        if (abortRef.current) abortRef.current.abort();
        abortRef.current = new AbortController();

        setMessagesLoading(true);
        try {
            const res = await getMessages(session.id, { page: 1, pageSize: PAGE_SIZE, signal: abortRef.current.signal });
            const data = res.data.data;

            if (data && data.items !== undefined) {
                const msgs = parseMessages(data.items);
                setMessages(msgs);
                setHasMoreMessages(data.hasNextPage ?? false);
                setMessagesPage(1);
            } else {
                setMessages(parseMessages(data));
            }
        } catch (err) {
            if (axios.isCancel(err)) return;
            toast.error('Mesajlar yüklenemedi.');
        } finally {
            setMessagesLoading(false);
        }
    };

    const loadMoreMessages = useCallback(async () => {
        if (!activeSession || loadingMore || !hasMoreMessages) return;
        setLoadingMore(true);
        const nextPage = messagesPage + 1;
        try {
            const res = await getMessages(activeSession.id, { page: nextPage, pageSize: PAGE_SIZE });
            const data = res.data.data;
            if (data?.items) {
                // Daha eski mesajlar listenin başına eklenir
                setMessages(prev => [...parseMessages(data.items), ...prev]);
                setHasMoreMessages(data.hasNextPage ?? false);
                setMessagesPage(nextPage);
            }
        } catch { toast.error('Daha fazla mesaj yüklenemedi.'); }
        finally { setLoadingMore(false); }
    }, [activeSession, loadingMore, hasMoreMessages, messagesPage]);

    const newChat = () => {
        setActiveSession(null);
        setMessages([]);
        setChunks([]);
        setShowChunks(false);
        setHasMoreMessages(false);
        setMessagesPage(1);
        if (window.innerWidth < 768) setSidebarCollapsed(true);
    };

    const handleSend = async (forcedQuestion) => {
        const q = (forcedQuestion ?? question).trim();
        if (!q || loading) return;
        if (!forcedQuestion) setQuestion('');
        setLoading(true);

        const optimisticId = nextMsgId();
        setMessages((prev) => [...prev, {
            role: 'User',
            content: q,
            id: optimisticId,
            createdAt: new Date().toISOString(),
            isOptimistic: true
        }]);

        abortRef.current = new AbortController();

        try {
            const res = await askQuestion(q, activeSession?.id || null, abortRef.current.signal);
            const { sessionId, answer, sourceChunks } = res.data.data;

            if (!activeSession) {
                const newSession = { id: sessionId, title: q.slice(0, 60), createdAt: new Date().toISOString() };
                setActiveSession(newSession);
                setSessions((prev) => [newSession, ...prev]);
            }

            const images = res.data.data.images || [];

            setMessages((prev) => [
                ...prev.filter(m => m.id !== optimisticId),
                {
                    role: 'User',
                    content: q,
                    id: nextMsgId(),
                    createdAt: new Date().toISOString()
                },
                {
                    role: 'Assistant',
                    content: answer,
                    id: nextMsgId(),
                    createdAt: new Date().toISOString(),
                    images: images.length > 0 ? images : undefined
                }
            ]);
            setChunks(sourceChunks || []);

            // Yeni mesaj sonrası en alta kaydır
            setTimeout(() => {
                virtuosoRef.current?.scrollToIndex({ index: 'LAST', behavior: 'smooth' });
            }, 50);
        } catch (err) {
            if (axios.isCancel(err) || err?.code === 'ERR_CANCELED') {
                setLoading(false);
                return;
            }
            toast.error(getRateLimitMessage(err));
            setMessages((prev) => [...prev, {
                role: 'Assistant',
                content: err?.response?.status === 429
                    ? '⏳ Sunucu meşgul. Lütfen birkaç saniye bekleyip tekrar deneyin.'
                    : 'Bir hata oluştu. Lütfen tekrar deneyin.',
                id: nextMsgId(),
                createdAt: new Date().toISOString(),
                isError: true,
                retryQuestion: q,
            }]);
        } finally { setLoading(false); }
    };

    const handleAbort = () => { abortRef.current?.abort(); };

    const handleCopy = (content, id) => {
        navigator.clipboard.writeText(content);
        setCopiedId(id);
        setTimeout(() => setCopiedId(null), 2000);
    };

    const handleDeleteSession = async (sessionId) => {
        setDeletingSessionId(sessionId);
        try {
            await deleteSession(sessionId);
            setSessions((prev) => prev.filter((s) => s.id !== sessionId));
            if (activeSession?.id === sessionId) newChat();
            toast.success('Sohbet silindi.');
        } catch { toast.error('Sohbet silinemedi.'); }
        finally { setDeletingSessionId(null); }
    };

    const handleStartRename = (session) => {
        setEditingSessionId(session.id);
        setEditingTitle(session.title);
    };

    const handleCommitRename = async (sessionId) => {
        const title = editingTitle.trim();
        setEditingSessionId(null);
        if (!title) return;
        setRenamingSessionId(sessionId);
        try {
            await renameSession(sessionId, title);
            setSessions((prev) => prev.map((s) => s.id === sessionId ? { ...s, title } : s));
            if (activeSession?.id === sessionId) setActiveSession((s) => ({ ...s, title }));
            toast.success('Sohbet adı güncellendi.');
        } catch { toast.error('Sohbet adı güncellenemedi.'); }
        finally { setRenamingSessionId(null); }
    };

    // Virtuoso için düz item dizisi
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
                <MessageBubble msg={item.msg} copiedId={copiedId} onCopy={handleCopy} onRetry={handleSend} />
            </div>
        );
    };

    return (
        <div style={{ display: 'flex', height: '100vh', background: 'var(--navy)' }}>
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
                onDeleteSession={handleDeleteSession}
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
                                    startReached={hasMoreMessages ? loadMoreMessages : undefined}
                                    components={{
                                        Header: hasMoreMessages ? () => (
                                            <div style={{ display: 'flex', justifyContent: 'center', padding: '8px 0' }}>
                                                <button
                                                    onClick={loadMoreMessages}
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
                                        // Son item typing indicator ise
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
                            onSend={handleSend}
                            loading={loading}
                            onAbort={handleAbort}
                        />
                    </div>

                    {showChunks && chunks.length > 0 && <SourcePanel chunks={chunks} />}
                </div>
            </div>
        </div>
    );
}
