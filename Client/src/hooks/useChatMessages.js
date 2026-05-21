import { useState, useRef, useCallback } from 'react';
import axios from 'axios';
import { askQuestion, getMessages } from '../services/api';
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
    const [showChunks, setShowChunks] = useState(false);
    const [copiedId, setCopiedId] = useState(null);
    const abortRef = useRef(null);
    const toast = useToast();

    const clearMessages = useCallback(() => {
        setMessages([]);
        setChunks([]);
        setShowChunks(false);
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

        const optimisticId = nextMsgId();
        setMessages(prev => [...prev, {
            role: 'User', content: q, id: optimisticId,
            createdAt: new Date().toISOString(), isOptimistic: true,
        }]);

        abortRef.current = new AbortController();

        try {
            const res = await askQuestion(q, activeSession?.id || null, abortRef.current.signal, skipClarification);
            const { sessionId, answer, sourceChunks, clarificationOptions, images: respImages } = res.data.data;

            if (!activeSession) {
                const newSession = { id: sessionId, title: q.slice(0, 60), createdAt: new Date().toISOString() };
                onNewSession?.(newSession);
                broadcastSessionCreated(newSession);
            }

            if (clarificationOptions?.length >= 2) {
                setMessages(prev => [
                    ...prev.filter(m => m.id !== optimisticId),
                    { role: 'User', content: q, id: nextMsgId(), createdAt: new Date().toISOString() },
                    { role: 'Assistant', content: '', id: nextMsgId(), createdAt: new Date().toISOString(), isClarification: true, clarificationOptions },
                ]);
                return;
            }

            const images = respImages || res.data.data.images || [];
            setMessages(prev => [
                ...prev.filter(m => m.id !== optimisticId && !m.isClarification),
                { role: 'User', content: q, id: nextMsgId(), createdAt: new Date().toISOString() },
                { role: 'Assistant', content: answer, id: nextMsgId(), createdAt: new Date().toISOString(), images: images.length > 0 ? images : undefined },
            ]);
            setChunks(sourceChunks || []);

            setTimeout(() => {
                virtuosoRef.current?.scrollToIndex({ index: 'LAST', behavior: 'smooth' });
            }, 50);
        } catch (err) {
            if (axios.isCancel(err) || err?.code === 'ERR_CANCELED') {
                return;  // finally bloğu setLoading(false) yapacak
            }
            // showApiError 429'da warning, diğerlerinde error toast atar
            showApiError(toast, err, getRateLimitMessage(err));
            setMessages(prev => [...prev, {
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
        showChunks, setShowChunks,
        copiedId,
        clearMessages,
        loadMessages,
        loadMoreMessages,
        handleSend,
        handleAbort,
        handleCopy,
    };
}
