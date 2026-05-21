import { useState, useCallback, useEffect } from 'react';
import { getSessions, deleteSession, deleteSessionsBatch, renameSession } from '../services/api';
import { useToast } from '../components/shared/Toast';
import { showApiError } from '../utils/format';
import {
    subscribeSessions,
    broadcastSessionDeleted,
    broadcastSessionRenamed,
} from '../lib/sessionsChannel';

export function useSessions() {
    const [sessions, setSessions] = useState([]);
    const [activeSession, setActiveSession] = useState(null);
    const [sessionsLoading, setSessionsLoading] = useState(true);
    const [editingSessionId, setEditingSessionId] = useState(null);
    const [editingTitle, setEditingTitle] = useState('');
    const [deletingSessionId, setDeletingSessionId] = useState(null);
    const [renamingSessionId, setRenamingSessionId] = useState(null);
    const toast = useToast();

    useEffect(() => {
        const unsubscribe = subscribeSessions((e) => {
            const data = e?.data;
            if (!data?.type) return;
            const { type, id, title, session } = data;

            if (type === 'session-deleted' && id) {
                setSessions(prev => prev.filter(s => s.id !== id));
                setActiveSession(prev => prev?.id === id ? null : prev);
            } else if (type === 'session-renamed' && id && typeof title === 'string') {
                setSessions(prev => prev.map(s => s.id === id ? { ...s, title } : s));
                setActiveSession(prev => prev?.id === id ? { ...prev, title } : prev);
            } else if (type === 'session-created' && session?.id) {
                setSessions(prev => prev.some(s => s.id === session.id) ? prev : [session, ...prev]);
            }
        });
        return unsubscribe;
    }, []);

    const fetchSessions = useCallback(async () => {
        setSessionsLoading(true);
        try {
            const res = await getSessions();
            setSessions(res.data.data || []);
        } catch (err) { showApiError(toast, err, 'Sohbet listesi yüklenemedi.'); }
        finally { setSessionsLoading(false); }
    }, [toast]);

    const handleBatchDeleteSessions = useCallback(async (ids, onDeleted) => {
        const idSet = new Set(ids);
        const snapshot = sessions;
        setSessions(prev => prev.filter(s => !idSet.has(s.id)));
        setActiveSession(prev => prev && idSet.has(prev.id) ? null : prev);
        try {
            const res = await deleteSessionsBatch(ids);
            const count = res.data?.data ?? ids.length;
            // Her silinen id için cross-tab broadcast
            ids.forEach(id => broadcastSessionDeleted(id));
            onDeleted?.(ids);
            toast.success(`${count} sohbet silindi.`);
            return count;
        } catch (err) {
            setSessions(snapshot);
            showApiError(toast, err, 'Sohbetler silinemedi.');
            throw err;
        }
    }, [sessions, toast]);

    const handleDeleteSession = useCallback(async (sessionId, onDeleted) => {
        setDeletingSessionId(sessionId);
        try {
            await deleteSession(sessionId);
            setSessions(prev => prev.filter(s => s.id !== sessionId));
            onDeleted?.(sessionId);
            broadcastSessionDeleted(sessionId);
            toast.success('Sohbet silindi.');
        } catch (err) { showApiError(toast, err, 'Sohbet silinemedi.'); }
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
            broadcastSessionRenamed(sessionId, title);
            toast.success('Sohbet adı güncellendi.');
        } catch (err) { showApiError(toast, err, 'Sohbet adı güncellenemedi.'); }
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
        handleBatchDeleteSessions,
        handleStartRename,
        handleCommitRename,
    };
}
