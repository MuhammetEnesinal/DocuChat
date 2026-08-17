import { useState, useCallback, useEffect } from 'react';
import {
    getSessions, deleteSession, deleteSessionsBatch, renameSession,
    archiveSession, unarchiveSession, pinSession, unpinSession,
    getArchivedSessionCount
} from '../services/api';
import { useToast } from '../components/shared/Toast';
import { showApiError } from '../lib/format';
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
    // Archive view toggle + count rozeti
    const [showArchived, setShowArchived] = useState(false);
    const [archivedCount, setArchivedCount] = useState(0);
    // Hangi session + hangi action busy (spinner sadece o butonda dönsün).
    // shape: { id: string, action: 'pin' | 'archive' | 'export' } | null
    const [busy, setBusy] = useState(null);
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

    // silent: gerçek zamanlı tazelemede skeleton flash'ı olmasın diye sessionsLoading değiştirilmez.
    const fetchSessions = useCallback(async (archivedView = null, silent = false) => {
        const useArchived = archivedView ?? showArchived;
        if (!silent) setSessionsLoading(true);
        try {
            // Default: aktif (archived=false). Archive view'da archived=true.
            const res = await getSessions({ archived: useArchived });
            const items = res.data.data || [];
            // Backend PaginatedResult ise items, IReadOnlyList ise direkt array
            setSessions(Array.isArray(items) ? items : (items.items || []));
        } catch (err) { if (!silent) showApiError(toast, err, 'Sohbet listesi yüklenemedi.'); }
        finally { if (!silent) setSessionsLoading(false); }
    }, [toast, showArchived]);

    const fetchArchivedCount = useCallback(async () => {
        try {
            const res = await getArchivedSessionCount();
            setArchivedCount(res.data?.data ?? 0);
        } catch { /* sessizce yut, badge yoksa yok */ }
    }, []);

    const toggleArchivedView = useCallback(async () => {
        const next = !showArchived;
        setShowArchived(next);
        // Yeni view'a göre listeyi yenile
        await fetchSessions(next);
    }, [showArchived, fetchSessions]);

    // Archive / Unarchive
    const handleArchiveSession = useCallback(async (sessionId) => {
        setBusy({ id: sessionId, action: 'archive' });
        try {
            await archiveSession(sessionId);
            setSessions(prev => prev.filter(s => s.id !== sessionId));
            setActiveSession(prev => prev?.id === sessionId ? null : prev);
            setArchivedCount(c => c + 1);
            toast.success('Sohbet arşivlendi.');
        } catch (err) { showApiError(toast, err, 'Sohbet arşivlenemedi.'); }
        finally { setBusy(null); }
    }, [toast]);

    const handleUnarchiveSession = useCallback(async (sessionId) => {
        setBusy({ id: sessionId, action: 'archive' });
        try {
            await unarchiveSession(sessionId);
            setSessions(prev => prev.filter(s => s.id !== sessionId));
            setArchivedCount(c => Math.max(0, c - 1));
            toast.success('Sohbet arşivden çıkarıldı.');
        } catch (err) { showApiError(toast, err, 'Geri çıkarma başarısız.'); }
        finally { setBusy(null); }
    }, [toast]);

    // Pin/Unpin sonrası listeyi client-side yeniden sıralar (sunucuya tekrar gitmeden).
    // Sıralama: pinned önce (PinnedAt desc), sonra son aktivite. Hata olursa fetchSessions
    // ile sunucu durumuna dönülür.
    const sortSessionsClientSide = useCallback((list) => {
        return [...list].sort((a, b) => {
            if (!!a.isPinned !== !!b.isPinned) return a.isPinned ? -1 : 1;
            if (a.isPinned && b.isPinned) {
                const ap = a.pinnedAt ? new Date(a.pinnedAt).getTime() : 0;
                const bp = b.pinnedAt ? new Date(b.pinnedAt).getTime() : 0;
                if (ap !== bp) return bp - ap;
            }
            // Non-pinned: son aktiviteye göre (updatedAt=son mesaj, yoksa createdAt) → yeni yazılan üstte
            const at = new Date(a.updatedAt || a.createdAt).getTime();
            const bt = new Date(b.updatedAt || b.createdAt).getTime();
            return bt - at;
        });
    }, []);

    // Bir sohbete mesaj yazılınca optimistik olarak "son aktivite"yi şimdi yapıp non-pinned
    // tepesine (sabitlerin altına) taşır. Backend de UpdatedAt bump ediyor; bu anlık karşılığı.
    const bumpSessionActivity = useCallback((sessionId) => {
        if (!sessionId) return;
        setSessions(prev => sortSessionsClientSide(
            prev.map(s => s.id === sessionId ? { ...s, updatedAt: new Date().toISOString() } : s)
        ));
    }, [sortSessionsClientSide]);

    const handlePinSession = useCallback(async (sessionId) => {
        setBusy({ id: sessionId, action: 'pin' });
        try {
            await pinSession(sessionId);
            setSessions(prev => sortSessionsClientSide(
                prev.map(s => s.id === sessionId
                    ? { ...s, isPinned: true, pinnedAt: new Date().toISOString() }
                    : s)
            ));
            toast.success('Sohbet sabitlendi.');
        } catch (err) {
            showApiError(toast, err, 'Sabitleme başarısız.');
            await fetchSessions();
        }
        finally { setBusy(null); }
    }, [toast, sortSessionsClientSide, fetchSessions]);

    const handleUnpinSession = useCallback(async (sessionId) => {
        setBusy({ id: sessionId, action: 'pin' });
        try {
            await unpinSession(sessionId);
            setSessions(prev => sortSessionsClientSide(
                prev.map(s => s.id === sessionId
                    ? { ...s, isPinned: false, pinnedAt: null }
                    : s)
            ));
            toast.success('Sabitleme kaldırıldı.');
        } catch (err) {
            showApiError(toast, err, 'Sabitleme kaldırılamadı.');
            await fetchSessions();
        }
        finally { setBusy(null); }
    }, [toast, sortSessionsClientSide, fetchSessions]);

    const handleBatchArchiveSessions = useCallback(async (ids) => {
        // İstekler sırayla gönderilir; her arşivleme bir veritabanı yazması olduğundan aynı anda
        // yığılmaları gereksiz yük yaratır. Oturum yazma uçlarının sunucu tarafındaki sınırı
        // kullanıcı bazlıdır ve elle yapılan çoklu seçime fazlasıyla yeter.
        // Listeden yalnız gerçekten arşivlenenler düşürülür; başarısız olanlar yerinde kalır.
        const archivedIds = new Set();
        let failed = 0;
        for (const id of ids) {
            setBusy({ id, action: 'archive' });
            try { await archiveSession(id); archivedIds.add(id); } catch { failed++; }
        }
        setBusy(null);
        if (archivedIds.size > 0) {
            setSessions(prev => prev.filter(s => !archivedIds.has(s.id)));
            setActiveSession(prev => prev && archivedIds.has(prev.id) ? null : prev);
            setArchivedCount(c => c + archivedIds.size);
            toast.success(`${archivedIds.size} sohbet arşivlendi.`);
        }
        if (failed > 0) toast.error(`${failed} sohbet arşivlenemedi.`);
        return archivedIds.size;
    }, [toast]);

    const handleBatchUnarchiveSessions = useCallback(async (ids) => {
        // Batch archive'ın simetriği — arşiv görünümünde çoklu seçimle geri çıkarma.
        const unarchivedIds = new Set();
        let failed = 0;
        for (const id of ids) {
            setBusy({ id, action: 'archive' });
            try { await unarchiveSession(id); unarchivedIds.add(id); } catch { failed++; }
        }
        setBusy(null);
        if (unarchivedIds.size > 0) {
            setSessions(prev => prev.filter(s => !unarchivedIds.has(s.id)));
            setArchivedCount(c => Math.max(0, c - unarchivedIds.size));
            toast.success(`${unarchivedIds.size} sohbet arşivden çıkarıldı.`);
        }
        if (failed > 0) toast.error(`${failed} sohbet çıkarılamadı.`);
        return unarchivedIds.size;
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
    };
}
