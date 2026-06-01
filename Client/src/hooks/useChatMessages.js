import { useState, useRef, useCallback } from 'react';
import axios from 'axios';
import { askQuestionStream, getMessages } from '../services/api';
import { getRateLimitMessage, showApiError } from '../utils/format';
import { useToast } from '../components/shared/Toast';
import { broadcastSessionCreated } from '../lib/sessionsChannel';

let _msgCounter = 0;
const nextMsgId = () => `msg_${Date.now()}_${++_msgCounter}`;
const PAGE_SIZE = 50;

function parseMessages(raw) {
    return (raw || []).map(m => ({
        ...m,
        images: m.imagesJson ? (() => {
            try { return JSON.parse(m.imagesJson); }
            catch (e) { console.warn('Resim JSON parse hatası:', e); return []; }
        })() : undefined,
    }));
}

export function useChatMessages(virtuosoRef) {
    const [messages, setMessages] = useState([]);
    const [loading, setLoading] = useState(false);
    const [messagesLoading, setMessagesLoading] = useState(false);
    const [hasMoreMessages, setHasMoreMessages] = useState(false);
    const [messagesPage, setMessagesPage] = useState(1);
    const [loadingMore, setLoadingMore] = useState(false);
    const [chunks, setChunks] = useState([]);
    const [copiedId, setCopiedId] = useState(null);
    const abortRef = useRef(null);
    const toast = useToast();

    const clearMessages = useCallback(() => {
        setMessages([]);
        setChunks([]);
        setHasMoreMessages(false);
        setMessagesPage(1);
    }, []);

    const loadMessages = useCallback(async (session) => {
        if (abortRef.current) abortRef.current.abort();
        abortRef.current = new AbortController();
        setMessagesLoading(true);
        try {
            const res = await getMessages(session.id, { page: 1, pageSize: PAGE_SIZE, signal: abortRef.current.signal });
            const data = res.data.data;
            if (data && data.items !== undefined) {
                setMessages(parseMessages(data.items));
                setHasMoreMessages(data.hasNextPage ?? false);
                setMessagesPage(1);
            } else {
                setMessages(parseMessages(data));
            }
        } catch (err) {
            if (axios.isCancel(err)) return;
            showApiError(toast, err, 'Mesajlar yüklenemedi.');
        } finally {
            setMessagesLoading(false);
        }
    }, [toast]);

    const loadMoreMessages = useCallback(async (activeSessionId) => {
        if (!activeSessionId || loadingMore || !hasMoreMessages) return;
        setLoadingMore(true);
        const nextPage = messagesPage + 1;
        try {
            const res = await getMessages(activeSessionId, { page: nextPage, pageSize: PAGE_SIZE });
            const data = res.data.data;
            if (data?.items) {
                setMessages(prev => [...parseMessages(data.items), ...prev]);
                setHasMoreMessages(data.hasNextPage ?? false);
                setMessagesPage(nextPage);
            }
        } catch (err) { showApiError(toast, err, 'Daha fazla mesaj yüklenemedi.'); }
        finally { setLoadingMore(false); }
    }, [loadingMore, hasMoreMessages, messagesPage, toast]);

    const handleSend = useCallback(async (q, activeSession, onNewSession, skipClarification = false) => {
        if (!q || loading) return;
        setLoading(true);

        // Optimistik mesajlar: hemen kullanıcı + boş asistan (streaming için)
        const userMsgId = nextMsgId();
        const assistantMsgId = nextMsgId();
        setMessages(prev => [
            ...prev.filter(m => !m.isClarification),
            { role: 'User', content: q, id: userMsgId, createdAt: new Date().toISOString() },
            { role: 'Assistant', content: '', id: assistantMsgId, createdAt: new Date().toISOString(), isStreaming: true },
        ]);

        abortRef.current = new AbortController();

        let receivedAnyToken = false;
        let receivedComplete = false;

        const onEvent = (evt) => {
            switch (evt.type) {
                case 'start': {
                    if (!activeSession && evt.sessionId) {
                        const newSession = { id: evt.sessionId, title: q.slice(0, 60), createdAt: new Date().toISOString() };
                        onNewSession?.(newSession);
                        broadcastSessionCreated(newSession);
                    }
                    break;
                }
                case 'cache_hit': {
                    receivedAnyToken = true;
                    receivedComplete = true;
                    setMessages(prev => prev.map(m => m.id === assistantMsgId ? {
                        ...m,
                        content: evt.answer || '',
                        images: evt.images && evt.images.length > 0 ? evt.images : undefined,
                        followUpQuestions: evt.followUps && evt.followUps.length > 0 ? evt.followUps : undefined,
                        isStreaming: false,
                    } : m));
                    break;
                }
                case 'clarification': {
                    // Asistan placeholder'ını clarification kartına dönüştür
                    setMessages(prev => prev.map(m => m.id === assistantMsgId ? {
                        ...m,
                        content: '',
                        isClarification: true,
                        clarificationOptions: evt.options || [],
                        isStreaming: false,
                    } : m));
                    break;
                }
                case 'token': {
                    receivedAnyToken = true;
                    if (evt.delta) {
                        setMessages(prev => prev.map(m => m.id === assistantMsgId
                            ? { ...m, content: (m.content || '') + evt.delta }
                            : m));
                    }
                    break;
                }
                case 'complete': {
                    receivedComplete = true;
                    setMessages(prev => prev.map(m => m.id === assistantMsgId ? {
                        ...m,
                        images: evt.images && evt.images.length > 0 ? evt.images : undefined,
                        followUpQuestions: evt.followUps && evt.followUps.length > 0 ? evt.followUps : undefined,
                        badge: evt.badge || undefined,
                        isStreaming: false,
                    } : m));
                    if (evt.chunks) setChunks(evt.chunks);
                    break;
                }
                case 'error': {
                    setMessages(prev => prev.map(m => m.id === assistantMsgId ? {
                        ...m,
                        content: m.content || 'Bir hata oluştu. Lütfen tekrar deneyin.',
                        isError: true,
                        retryQuestion: q,
                        isStreaming: false,
                    } : m));
                    toast.error(evt.message || 'Bir hata oluştu');
                    break;
                }
                case 'done': {
                    // İletim bitti; setLoading finally bloğunda yapılıyor
                    break;
                }
                default: break;
            }
        };

        try {
            const result = await askQuestionStream(
                { question: q, sessionId: activeSession?.id || null, skipClarification },
                onEvent,
                abortRef.current.signal
            );

            if (result.aborted) {
                // Kullanıcı iptal etti — boş kalan placeholder'ı sil veya partial content'i koru
                setMessages(prev => prev.map(m => m.id === assistantMsgId
                    ? { ...m, isStreaming: false, isAborted: !receivedAnyToken }
                    : m));
                return;
            }
            if (!result.ok) {
                const msg = result.error || 'Bir hata oluştu';
                const is429 = msg.includes('429');
                toast.error(is429 ? '⏳ Sunucu meşgul. Birkaç saniye sonra tekrar deneyin.' : 'Bir hata oluştu');
                setMessages(prev => prev.map(m => m.id === assistantMsgId ? {
                    ...m,
                    content: is429
                        ? '⏳ Sunucu meşgul. Lütfen birkaç saniye bekleyip tekrar deneyin.'
                        : 'Bir hata oluştu. Lütfen tekrar deneyin.',
                    isError: true,
                    retryQuestion: q,
                    isStreaming: false,
                } : m));
                return;
            }

            // İletim bitti ama complete event hiç gelmedi (kısa cevap veya cache_hit)
            if (!receivedComplete) {
                setMessages(prev => prev.map(m => m.id === assistantMsgId
                    ? { ...m, isStreaming: false }
                    : m));
            }

            setTimeout(() => {
                virtuosoRef.current?.scrollToIndex({ index: 'LAST', behavior: 'smooth' });
            }, 50);
        } catch (err) {
            if (axios.isCancel(err) || err?.code === 'ERR_CANCELED' || err?.name === 'AbortError') {
                setMessages(prev => prev.map(m => m.id === assistantMsgId
                    ? { ...m, isStreaming: false, isAborted: !receivedAnyToken }
                    : m));
                return;
            }
            showApiError(toast, err, getRateLimitMessage(err));
            setMessages(prev => prev.map(m => m.id === assistantMsgId ? {
                ...m,
                content: 'Bir hata oluştu. Lütfen tekrar deneyin.',
                isError: true,
                retryQuestion: q,
                isStreaming: false,
            } : m));
        } finally { setLoading(false); }
    }, [loading, toast, virtuosoRef]);

    const handleAbort = useCallback(() => { abortRef.current?.abort(); }, []);

    const handleCopy = useCallback((content, id) => {
        navigator.clipboard.writeText(content);
        setCopiedId(id);
        setTimeout(() => setCopiedId(null), 2000);
    }, []);

    return {
        messages, setMessages,
        loading,
        messagesLoading,
        hasMoreMessages,
        messagesPage,
        loadingMore,
        chunks, setChunks,
        copiedId,
        clearMessages,
        loadMessages,
        loadMoreMessages,
        handleSend,
        handleAbort,
        handleCopy,
    };
}
