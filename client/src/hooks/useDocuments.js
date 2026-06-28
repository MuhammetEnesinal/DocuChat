import { useState, useCallback, useRef, useEffect } from 'react';
import { getDocuments, uploadDocument, deleteDocument, deleteDocumentsBatch, getDocumentChunks, reprocessDocument, reprocessDocumentsBatch, downloadDocument } from '../services/api';
import { useToast } from '../components/shared/Toast';
import { showApiError, getApiErrorMessage } from '../utils/format';

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
        } catch (err) { if (!silent) showApiError(toast, err, 'Belgeler yüklenemedi.'); }
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
        } catch (err) {
            fetchDocs(true);
            throw err;
        }
    }, [selectedDoc, fetchDocs]);

    // Çoklu yeniden işleme — TEK HTTP isteği (batch-reprocess). Backend queue'ya enqueue eder,
    // consumer maxConcurrent ile throttle yapar; client side rate-limit'e takılmaz.
    const batchReprocessDocs = useCallback(async (ids) => {
        if (ids.length === 0) return 0;
        // Optimistic: hepsini Pending yap (backend de aynısını yapar — consumer alınca Processing'e döner)
        setDocuments(prev => prev.map(d => ids.includes(d.id) ? { ...d, status: 'Pending' } : d));
        try {
            const res = await reprocessDocumentsBatch(ids);
            const queued = res.data?.data ?? 0;
            const skipped = ids.length - queued;
            if (queued > 0) toast.success(`${queued} belge yeniden işleme alındı.`);
            if (skipped > 0) toast.info?.(`${skipped} belge atlandı (zaten işleniyor veya bulunamadı).`);
            fetchDocs(true);  // gerçek statusları çek
            return queued;
        } catch (err) {
            showApiError(toast, err, 'Belgeler yeniden işlenemedi.');
            fetchDocs(true);  // optimistic rollback için sunucu durumunu çek
            return 0;
        }
    }, [toast, fetchDocs]);

    // Çoklu indirme — her belgeyi blob olarak çek, browser'da download tetikle
    const batchDownloadDocs = useCallback(async (docs) => {
        if (docs.length === 0) return 0;
        let success = 0, failed = 0;
        for (const doc of docs) {
            try {
                const res = await downloadDocument(doc.id);
                const url = URL.createObjectURL(res.data);
                const a = document.createElement('a');
                a.href = url;
                a.download = doc.fileName;
                document.body.appendChild(a);
                a.click();
                a.remove();
                URL.revokeObjectURL(url);
                success++;
                await new Promise(r => setTimeout(r, 200));  // browser'a download arası nefes
            } catch {
                failed++;
            }
        }
        if (success > 0) toast.success(`${success} belge indirildi.`);
        if (failed > 0) toast.error(`${failed} belge indirilemedi.`);
        return success;
    }, [toast]);

    const batchDeleteDocs = useCallback(async (ids) => {
        const idSet = new Set(ids);
        const snapshot = documents;
        setDocuments(prev => prev.filter(d => !idSet.has(d.id)));
        if (selectedDoc && idSet.has(selectedDoc.id)) setShowChunksModal(false);
        try {
            const res = await deleteDocumentsBatch(ids);
            const count = res.data?.data ?? ids.length;
            toast.success(`${count} belge silindi.`);
            fetchDocs(true);
            return count;
        } catch (err) {
            setDocuments(snapshot);
            showApiError(toast, err, 'Belgeler silinemedi.');
            throw err;
        }
    }, [documents, selectedDoc, toast, fetchDocs]);

    const handleViewChunks = useCallback(async (doc) => {
        setSelectedDoc(doc);
        setShowChunksModal(true);
        setChunksLoading(true);
        setChunks([]);
        try {
            const res = await getDocumentChunks(doc.id);
            setChunks(res.data.data || []);
        } catch (err) { showApiError(toast, err, "Chunk'lar yüklenemedi."); }
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
            const msg = getApiErrorMessage(err, `"${file.name}" yüklenemedi.`);
            setUploads(prev => prev.map(u => u.id === uid ? { ...u, status: 'error', error: msg } : u));
            showApiError(toast, err, `"${file.name}" yüklenemedi.`);
            setTimeout(() => setUploads(prev => prev.filter(u => u.id !== uid)), 5000);
        }
    }, [toast, fetchDocs]);

    const processFiles = useCallback((files) => {
        const allowed = ['application/pdf', 'application/msword',
            'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
            'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
            'text/csv'];
        const MAX_SIZE = 50 * 1024 * 1024;
        const valid = [];
        const rejected = [];
        for (const f of Array.from(files)) {
            if (!allowed.includes(f.type)) { rejected.push({ name: f.name, reason: 'format' }); continue; }
            if (f.size > MAX_SIZE) { rejected.push({ name: f.name, reason: 'boyut' }); continue; }
            valid.push(f);
        }

        // Toplu mesaj: birden fazla dosya reddedildiyse spam yapma, tek toast ile özetle.
        if (rejected.length === 1) {
            const r = rejected[0];
            toast.error(r.reason === 'format'
                ? `"${r.name}" desteklenmeyen format.`
                : `"${r.name}" 50 MB'dan büyük olamaz.`);
        } else if (rejected.length > 1) {
            const names = rejected.slice(0, 3).map(r => `${r.name} (${r.reason})`).join(', ');
            const suffix = rejected.length > 3 ? ` ve ${rejected.length - 3} dosya daha` : '';
            toast.error(`${rejected.length} dosya reddedildi: ${names}${suffix}.`);
        }

        if (valid.length === 0) return;

        // Throttled upload — max 3 paralel, sonra sıradakiler.
        // Sebep: backend upload rate limit (10/dk) + Mistral OCR rate limit + browser network sıkışması.
        // 3 paralel = backend rahat işler, kullanıcı progress'i akıcı görür.
        (async () => {
            const BATCH = 5;
            for (let i = 0; i < valid.length; i += BATCH) {
                const batch = valid.slice(i, i + BATCH);
                await Promise.all(batch.map(uploadFile));
            }
        })();
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
        batchDeleteDocs,
        batchReprocessDocs,
        batchDownloadDocs,
        handleViewChunks,
        uploadFile,
        processFiles,
    };
}
