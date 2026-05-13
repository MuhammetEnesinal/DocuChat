import { useState, useCallback, useEffect, useRef } from 'react';
import { getSessions, deleteSession, renameSession } from '../services/api';
import { useToast } from '../components/shared/Toast';

const CHANNEL_NAME = 'docuchat-sessions';

export function useSessions() {
    const [sessions, setSessions] = useState([]);
    const [activeSession, setActiveSession] = useState(null);
    const [sessionsLoading, setSessionsLoading] = useState(true);
    const [editingSessionId, setEditingSessionId] = useState(null);
    const [editingTitle, setEditingTitle] = useState('');
    const [deletingSessionId, setDeletingSessionId] = useState(null);
    const [renamingSessionId, setRenamingSessionId] = useState(null);
    const toast = useToast();
    const channelRef = useRef(null);

    useEffect(() => {
        if (!('BroadcastChannel' in window)) return;
        channelRef.current = new BroadcastChannel(CHANNEL_NAME);
        channelRef.current.onmessage = (e) => {
            const { type, id, title } = e.data;
            if (type === 'session-deleted') {
                setSessions(prev => prev.filter(s => s.id !== id));
                setActiveSession(prev => prev?.id === id ? null : prev);
            } else if (type === 'session-renamed') {
                setSessions(prev => prev.map(s => s.id === id ? { ...s, title } : s));
                setActiveSession(prev => prev?.id === id ? { ...prev, title } : prev);
            } else if (type === 'session-created') {
                setSessions(prev => prev.some(s => s.id === id) ? prev : [e.data.session, ...prev]);
            }
        };
        return () => { channelRef.current?.close(); };
    }, []);

    const fetchSessions = useCallback(async () => {
        setSessionsLoading(true);
        try {
            const res = await getSessions();
            setSessions(res.data.data || []);
        } catch { toast.error('Sohbet listesi yüklenemedi.'); }
        finally { setSessionsLoading(false); }
    }, [toast]);

    const handleDeleteSession = useCallback(async (sessionId, onDeleted) => {
        setDeletingSessionId(sessionId);
        try {
            await deleteSession(sessionId);
            setSessions(prev => prev.filter(s => s.id !== sessionId));
            onDeleted?.(sessionId);
            channelRef.current?.postMessage({ type: 'session-deleted', id: sessionId });
            toast.success('Sohbet silindi.');
        } catch { toast.error('Sohbet silinemedi.'); }
        finally { setDeletingSessionId(null); }
    }, [toast]);

    const handleStartRename = useCallback((session) => {
        setEditingSessionId(session.id);
        setEditingTitle(session.title);
    }, []);

    const handleCommitRename = useCallback(async (sessionId) => {
        const title = editingTitle.trim();
        setEditingSessionId(null);
        if (!title) return;
        setRenamingSessionId(sessionId);
        try {
            await renameSession(sessionId, title);
            setSessions(prev => prev.map(s => s.id === sessionId ? { ...s, title } : s));
            setActiveSession(s => s?.id === sessionId ? { ...s, title } : s);
            channelRef.current?.postMessage({ type: 'session-renamed', id: sessionId, title });
            toast.success('Sohbet adı güncellendi.');
        } catch { toast.error('Sohbet adı güncellenemedi.'); }
        finally { setRenamingSessionId(null); }
    }, [editingTitle, toast]);

    return {
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
    };
}
