import { useState, useCallback, useRef, useEffect } from 'react';
import { getDocuments, uploadDocument, deleteDocument, getDocumentChunks } from '../services/api';
import { useToast } from '../components/shared/Toast';

export function useDocuments() {
    const [documents, setDocuments] = useState([]);
    const [docsLoading, setDocsLoading] = useState(true);
    const [uploads, setUploads] = useState([]);
    const [docSearch, setDocSearch] = useState('');
    const [dragOver, setDragOver] = useState(false);
    const [selectedDoc, setSelectedDoc] = useState(null);
    const [chunks, setChunks] = useState([]);
    const [chunksLoading, setChunksLoading] = useState(false);
    const [showChunksModal, setShowChunksModal] = useState(false);
    const toast = useToast();
    const pollTimerRef = useRef(null);

    useEffect(() => {
        return () => { if (pollTimerRef.current) clearTimeout(pollTimerRef.current); };
    }, []);

    const fetchDocs = useCallback(async (silent = false, search = '') => {
        if (pollTimerRef.current) { clearTimeout(pollTimerRef.current); pollTimerRef.current = null; }
        if (!silent) setDocsLoading(true);
        try {
            const params = search ? { search } : {};
            const res = await getDocuments(params);
            const docs = res.data.data || [];
            setDocuments(docs);
            if (docs.some(d => d.status === 'Processing' || d.status === 'Pending'))
                pollTimerRef.current = setTimeout(() => fetchDocs(true, search), 3000);
        } catch { if (!silent) toast.error('Belgeler yüklenemedi.'); }
        finally { if (!silent) setDocsLoading(false); }
    }, [toast]);

    const searchTimerRef = useRef(null);
    const handleDocSearch = useCallback((value) => {
        setDocSearch(value);
        if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
        searchTimerRef.current = setTimeout(() => fetchDocs(false, value), 300);
    }, [fetchDocs]);

    const deleteDoc = useCallback(async (id) => {
        setDocuments(prev => prev.filter(d => d.id !== id));
        if (selectedDoc?.id === id) setShowChunksModal(false);
        try {
            await deleteDocument(id);
        } catch {
            fetchDocs(true);
            throw new Error('delete_failed');
        }
    }, [selectedDoc, fetchDocs]);

    const handleViewChunks = useCallback(async (doc) => {
        setSelectedDoc(doc);
        setShowChunksModal(true);
        setChunksLoading(true);
        setChunks([]);
        try {
            const res = await getDocumentChunks(doc.id);
            setChunks(res.data.data || []);
        } catch { toast.error("Chunk'lar yüklenemedi."); }
        finally { setChunksLoading(false); }
    }, [toast]);

    const uploadFile = useCallback(async (file) => {
        const uid = Date.now() + Math.random();
        setUploads(prev => [...prev, { id: uid, name: file.name, progress: 0, status: 'uploading' }]);
        try {
            await uploadDocument(file, (p) => setUploads(prev => prev.map(u => u.id === uid ? { ...u, progress: p } : u)));
            setUploads(prev => prev.map(u => u.id === uid ? { ...u, progress: 100, status: 'done' } : u));
            toast.success(`"${file.name}" yüklendi.`);
            fetchDocs(true);
            setTimeout(() => setUploads(prev => prev.filter(u => u.id !== uid)), 3000);
        } catch (err) {
            const msg = err.response?.data?.error?.message || err.message;
            setUploads(prev => prev.map(u => u.id === uid ? { ...u, status: 'error', error: msg } : u));
            toast.error(`"${file.name}" yüklenemedi.`);
            setTimeout(() => setUploads(prev => prev.filter(u => u.id !== uid)), 5000);
        }
    }, [toast, fetchDocs]);

    const processFiles = useCallback((files) => {
        const allowed = ['application/pdf', 'application/msword',
            'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
            'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
            'text/plain', 'text/csv'];
        const MAX_SIZE = 50 * 1024 * 1024;
        const valid = [];
        for (const f of Array.from(files)) {
            if (!allowed.includes(f.type)) { toast.error(`"${f.name}" desteklenmeyen format.`); continue; }
            if (f.size > MAX_SIZE) { toast.error(`"${f.name}" 50 MB'dan büyük olamaz.`); continue; }
            valid.push(f);
        }
        if (valid.length === 0) return;
        valid.forEach(uploadFile);
    }, [toast, uploadFile]);

    return {
        documents, setDocuments,
        docsLoading,
        uploads,
        docSearch, setDocSearch: handleDocSearch,
        dragOver, setDragOver,
        selectedDoc,
        chunks,
        chunksLoading,
        showChunksModal, setShowChunksModal,
        fetchDocs,
        deleteDoc,
        handleViewChunks,
        uploadFile,
        processFiles,
    };
}
