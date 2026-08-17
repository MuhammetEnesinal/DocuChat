import { useState, useEffect, useRef, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { Virtuoso } from 'react-virtuoso';
import { getPopularQuestions } from '../services/api';
import { formatDateShort } from '../lib/format';
import { useToast } from '../components/shared/Toast';
import { useAuth } from '../hooks/useAuth';
import { useSessions } from '../hooks/useSessions';
import { useChatMessages } from '../hooks/useChatMessages';
import { useRealtimeEvent } from '../hooks/useRealtime';
import { onRealtimeReconnected } from '../lib/realtime';
import { RealtimeEvents } from '../lib/realtimeEvents';
import ChatSidebar from '../components/chat/ChatSidebar';
import MessageBubble from '../components/chat/MessageBubble';
import NewChatHero from '../components/chat/NewChatHero';
import ChatInput from '../components/chat/ChatInput';
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
    const [confirmBatch, setConfirmBatch] = useState(null);

    const virtuosoRef = useRef(null);
    const inputRef = useRef(null);
    const skipNextClarificationRef = useRef(false);
    // Bu tab bir mesaj gönderdiğinde, kendi tetiklediği chat.message.added sinyalini görmezden
    // gelmek için son gönderim bitiş zamanı. Aksi halde gönderen tab, kendi akışını (follow-up
    // chip'leri, badge — DB'de tutulmayan geçici UI) DB reload'u ile ezerdi. Diğer pencereler
    // (gönderim yapmayan) bu ref'i 0 gördüğü için anında tazeler.
    const lastSendEndRef = useRef(0);
    const { user, logout } = useAuth();
    const navigate = useNavigate();
    const toast = useToast();

    const {
        sessions, setSessions,
        sortSessionsClientSide,
        bumpSessionActivity,
        activeSession, setActiveSession,
        sessionsLoading,
        editingSessionId, setEditingSessionId,
        editingTitle, setEditingTitle,
        deletingSessionId,
        renamingSessionId,
        fetchSessions,
        handleDeleteSession,
        handleBatchDeleteSessions,
        handleStartRename,
        handleCommitRename,
        // Archive / Pin / Export
        showArchived,
        archivedCount,
        busy,
        toggleArchivedView,
        fetchArchivedCount,
        handleArchiveSession,
        handleUnarchiveSession,
        handlePinSession,
        handleUnpinSession,
        handleBatchArchiveSessions,
        handleBatchUnarchiveSessions,
    } = useSessions();

    const {
        messages, setMessages,
        loading,
        messagesLoading,
        hasMoreMessages,
        loadingMore,
        copiedId,
        clearMessages,
        loadMessages,
        loadMoreMessages,
        handleSend,
        handleAbort,
        handleCopy,
    } = useChatMessages(virtuosoRef);

    // Popüler sorular DEPARTMAN-kapsamlı (backend cache'ten). Departman değişince (token yenileme
    // sonrası reconnect) yeniden çekilir ki öneriler yeni departmanı yansıtsın.
    const fetchPopular = useCallback(async ({ silent = false } = {}) => {
        if (!silent) setPopularQuestionsLoading(true);
        try {
            const res = await getPopularQuestions(6);
            setPopularQuestions(res.data.data || []);
        } catch (err) {
            if (err?.code === 'ERR_CANCELED' || err?.name === 'CanceledError') return;
            if (err?.response?.status === 401) return;
            console.warn('[Chat] Popüler sorular yüklenemedi:', err);
        }
        finally { if (!silent) setPopularQuestionsLoading(false); }
    }, []);

    useEffect(() => {
        fetchSessions();
        fetchArchivedCount();
        fetchPopular();
    }, [fetchSessions, fetchPopular]);

    useEffect(() => {
        const handleResize = () => { if (window.innerWidth < 768) setSidebarCollapsed(true); };
        window.addEventListener('resize', handleResize);
        return () => window.removeEventListener('resize', handleResize);
    }, []);

    // ── Gerçek zamanlı (SignalR) canlılık ──
    // chat.session.changed → sohbet listesi + arşiv sayacı sessizce tazelenir (başka sekme/cihaz
    //   yeni sohbet açtı / adı değişti / arşivledi / sildi).
    // chat.message.added   → açık olan sohbete başka pencere/cihaz mesaj eklediyse, mesajlar
    //   sessizce yeniden çekilir. Gönderen tab kendi sinyalini lastSendEndRef ile atlar (kendi
    //   akışını ezmesin: follow-up chip'leri / badge DB'de yok).
    useRealtimeEvent((evt) => {
        if (evt?.type === RealtimeEvents.ChatSessionChanged) {
            fetchSessions(null, true);   // silent
            fetchArchivedCount();
        } else if (evt?.type === RealtimeEvents.ChatMessageAdded) {
            const sid = evt.payload?.sessionId;
            if (sid && sid === activeSession?.id && !loading &&
                Date.now() - lastSendEndRef.current > 5000) {
                loadMessages(activeSession, { silent: true });
            }
        }
    });

    // Yeniden bağlanınca kaçmış olabilecek sinyalleri telafi: liste + açık sohbeti sessizce tazele.
    useEffect(() => {
        return onRealtimeReconnected(() => {
            fetchSessions(null, true);
            fetchArchivedCount();
            fetchPopular({ silent: true });   // departman değişmiş olabilir → öneriler yeni kapsamdan
            if (activeSession && !loading) loadMessages(activeSession, { silent: true });
        });
    }, [activeSession, loading, fetchSessions, fetchArchivedCount, fetchPopular, loadMessages]);

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
        const skip = skipClarification || skipNextClarificationRef.current;
        skipNextClarificationRef.current = false;
        // Mevcut sohbete yazıldıysa optimistik olarak hemen "son aktivite" yap → non-pinned tepesine
        // (sabitlerin altına) taşı. Yeni sohbet ise callback'te sort'lu eklenir.
        if (activeSession) bumpSessionActivity(activeSession.id);
        const res = await handleSend(q, activeSession, (newSession) => {
            setActiveSession(newSession);
            setSessions(prev => sortSessionsClientSide([newSession, ...prev]));
        }, skip);
        // Gönderim bitti → kendi realtime sinyalini (bu tab kaynaklı) kısa süre görmezden gel.
        lastSendEndRef.current = Date.now();
        // İlk soru (yeni sohbet) iptal edilip hiç cevap kaydedilmediyse: boş session'ı sidebar'dan
        // kaldır ve temiz anasayfaya dön (backend session'ı zaten sildi).
        if (res?.aborted && res.createdSessionId && !res.hadComplete) {
            setSessions(prev => prev.filter(s => s.id !== res.createdSessionId));
            newChat();
        }
        // Netleştirme YENİ sohbet için döndü → backend boş session'ı sildi. Sidebar'dan kaldır ve
        // aktif session'ı sıfırla; kullanıcı bir seçenek seçerse yeni istek yeni session açar.
        // Mesajları KORU — clarification kartı ekranda kalsın (newChat çağırma).
        else if (res?.clarifiedNewSession && res.createdSessionId) {
            setSessions(prev => prev.filter(s => s.id !== res.createdSessionId));
            setActiveSession(null);
        }
    };

    const onClarificationSelect = (opt) => {
        // Belirsiz soru + clarification balonunu HEMEN kaldır (cevabı bekleme); yerine
        // seçilen TAM soru tek kullanıcı balonu olarak gönderilir. Böylece eski belirsiz
        // soru ekranda kalmaz, çift balon olmaz.
        setMessages(prev => {
            const idx = prev.findIndex(m => m.isClarification);
            if (idx === -1) return prev;
            const precedingId = idx > 0 ? prev[idx - 1].id : null;
            return prev.filter(m => m.id !== prev[idx].id && m.id !== precedingId);
        });
        onSend(opt, true);
    };

    // Follow-up sorusu zaten tam/bağımsız bir cümle → clarification atlanır.
    // Tıklanınca o mesajın chip'leri HEMEN kaldırılır (basınca kaybolsun).
    const onFollowUpSelect = (q, msgId) => {
        if (msgId != null)
            setMessages(prev => prev.map(m => m.id === msgId ? { ...m, followUpQuestions: undefined } : m));
        onSend(q, true);
    };

    // Feedback verildi → mesajın feedbackGiven state'i güncellensin (UI optimistik)
    const onFeedbackGiven = (msgId, rating) => {
        setMessages(prev => prev.map(m => m.id === msgId ? { ...m, feedbackGiven: rating } : m));
    };

    const onClarificationDismiss = (clarificationMsgId) => {
        setMessages(prev => {
            const idx = prev.findIndex(m => m.id === clarificationMsgId);
            const preceding = idx > 0 ? prev[idx - 1] : null;
            const originalQ = preceding?.role === 'User' ? preceding.content : null;
            if (originalQ) setQuestion(originalQ);
            return prev.filter(m => m.id !== clarificationMsgId && m.id !== preceding?.id);
        });
        skipNextClarificationRef.current = true;
        setTimeout(() => inputRef.current?.focus(), 50);
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

    const onBatchDeleteSessions = (ids, exitSelectMode) => {
        setConfirmBatch({ ids, exitSelectMode });
    };

    const confirmBatchDelete = async () => {
        const { ids, exitSelectMode } = confirmBatch;
        setConfirmBatch(null);
        try {
            await handleBatchDeleteSessions(ids, (deletedIds) => {
                if (activeSession && deletedIds.includes(activeSession.id)) newChat();
            });
            exitSelectMode?.();
        } catch { /* toast hook'ta atıldı */ }
    };

    // useMemo: input'a her tuş vuruşu Chat'i render eder; mesaj listesi değişmediyse
    // separator hesabı tekrarlanmasın (uzun sohbetlerde gereksiz CPU).
    const virtualItems = useMemo(() => buildVirtualItems(messages), [messages]);

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
                <MessageBubble msg={item.msg} copiedId={copiedId} onCopy={handleCopy} onRetry={onSend} onClarificationSelect={onClarificationSelect} onClarificationDismiss={onClarificationDismiss} onFollowUpSelect={onFollowUpSelect} onFeedbackGiven={onFeedbackGiven} />
            </div>
        );
    };

    return (
        <div style={{ display: 'flex', height: '100dvh', background: '#000' }}>
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
                onBatchDeleteSessions={onBatchDeleteSessions}
                user={user}
                onLogout={() => { logout(); navigate('/login'); }}
                // Archive / Pin / Export
                showArchived={showArchived}
                archivedCount={archivedCount}
                busy={busy}
                onToggleArchived={toggleArchivedView}
                onArchive={handleArchiveSession}
                onUnarchive={handleUnarchiveSession}
                onPin={handlePinSession}
                onUnpin={handleUnpinSession}
                onBatchArchiveSessions={handleBatchArchiveSessions}
                onBatchUnarchiveSessions={handleBatchUnarchiveSessions}
            />

            <div style={{ display: 'flex', flexDirection: 'column', flex: 1, minWidth: 0 }}>
                <div className="chat-header glass" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '0 24px', borderBottom: '1px solid var(--glass-border)', position: 'relative', height: '74px', flexShrink: 0 }}>
                    <div style={{ minWidth: 0, display: 'flex', alignItems: 'center', gap: '10px' }}>
                        <span style={{ width: '6px', height: '6px', borderRadius: '50%', background: '#22c55e', boxShadow: '0 0 8px rgba(34,197,94,0.6)', flexShrink: 0 }} />
                        <h1 style={{ fontSize: '15px', fontWeight: 600, color: 'var(--text-primary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', margin: 0, letterSpacing: '-0.01em' }}>
                            {activeSession?.title || 'Yeni Sohbet'}
                        </h1>
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexShrink: 0, marginLeft: '12px' }} />
                    <div className="gradient-beam" style={{ position: 'absolute', left: 0, right: 0, bottom: 0 }} />
                </div>

                <div className={messages.length > 0 ? 'violet-dome-soft' : ''} style={{ display: 'flex', flex: 1, minHeight: 0 }}>
                    <div style={{ display: 'flex', flexDirection: 'column', flex: 1, minWidth: 0 }}>
                        <div className={messages.length > 0 ? 'messages-fade' : ''} style={{ flex: 1, minHeight: 0 }}>
                            {messagesLoading ? (
                                <div style={{ padding: '24px' }}>
                                    <MessageSkeleton />
                                </div>
                            ) : messages.length === 0 ? (
                                <NewChatHero
                                    popularQuestions={popularQuestions}
                                    onSelectQuestion={(q) => { setQuestion(q); setTimeout(() => inputRef.current?.focus(), 50); }}
                                >
                                    <ChatInput
                                        value={question}
                                        onChange={setQuestion}
                                        onSend={onSend}
                                        loading={loading}
                                        onAbort={handleAbort}
                                        inputRef={inputRef}
                                    />
                                </NewChatHero>
                            ) : (
                                <Virtuoso
                                    ref={virtuosoRef}
                                    style={{ height: '100%' }}
                                    totalCount={virtualItems.length}
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
                                    itemContent={(index) => (
                                        <div style={{ padding: '0 24px' }}>
                                            {renderVirtualItem(index)}
                                        </div>
                                    )}
                                />
                            )}
                        </div>

                        {messages.length > 0 && (
                            <ChatInput
                                value={question}
                                onChange={setQuestion}
                                onSend={onSend}
                                loading={loading}
                                onAbort={handleAbort}
                                inputRef={inputRef}
                            />
                        )}
                    </div>

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

            {confirmBatch && (
                <ConfirmDialog
                    title="Sohbetleri Sil"
                    message={`${confirmBatch.ids.length} sohbet kalıcı olarak silinecek. Emin misiniz?`}
                    confirmLabel="Sil"
                    onConfirm={confirmBatchDelete}
                    onCancel={() => setConfirmBatch(null)}
                />
            )}
        </div>
    );
}
