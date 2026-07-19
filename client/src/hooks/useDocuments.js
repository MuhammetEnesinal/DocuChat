import { useState, useCallback, useRef, useEffect } from 'react';
import { getDocuments, uploadDocument, deleteDocument, deleteDocumentsBatch, reprocessDocument, reprocessDocumentsBatch, downloadDocument } from '../services/api';
import { useToast } from '../components/shared/Toast';
import { showApiError, getApiErrorMessage } from '../lib/format';

const PAGE_SIZE = 20;

export function useDocuments() {
    const [documents, setDocuments] = useState([]);
    const [docsLoading, setDocsLoading] = useState(true);
    const [uploads, setUploads] = useState([]);
    const [docSearch, setDocSearch] = useState('');
    const [dragOver, setDragOver] = useState(false);
    // Belgelerin yükleneceği departman — DocumentUpload seçicisinden gelir. Zorunlu.
    const [uploadDepartmentId, setUploadDepartmentId] = useState('');
    // Server-side pagination
    const [page, setPage] = useState(1);
    const [totalCount, setTotalCount] = useState(0);      // aktif filtreye göre (arama dahil)
    const [grandTotal, setGrandTotal] = useState(0);      // aramasız GENEL toplam — sekme rozeti için
    const pageRef = useRef(1);      // fetchDocs setTimeout/poll içinden güncel sayfayı okur
    const searchRef = useRef('');   // aynı şekilde güncel aramayı okur
    const toast = useToast();
    const pollTimerRef = useRef(null);
    const searchTimerRef = useRef(null);

    useEffect(() => {
        return () => {
            if (pollTimerRef.current) clearTimeout(pollTimerRef.current);
            if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
        };
    }, []);

    const fetchDocs = useCallback(async (silent = false) => {
        if (pollTimerRef.current) { clearTimeout(pollTimerRef.current); pollTimerRef.current = null; }
        if (!silent) setDocsLoading(true);
        try {
            const params = { page: pageRef.current, pageSize: PAGE_SIZE };
            if (searchRef.current) params.search = searchRef.current;
            const res = await getDocuments(params);
            const data = res.data.data || {};
            const items = data.items || [];
            const total = data.totalCount ?? items.length;

            // Geçerli sayfa artık aralık dışında kaldıysa (son sayfadaki son öğe silindi vb.)
            // gerçek son sayfaya çekip yeniden getir.
            const lastPage = Math.max(1, Math.ceil(total / PAGE_SIZE));
            if (pageRef.current > lastPage) {
                pageRef.current = lastPage;
                setPage(lastPage);
                return fetchDocs(silent);
            }

            setDocuments(items);
            setTotalCount(total);
            if (!searchRef.current) setGrandTotal(total);  // arama yokken gelen total = genel toplam
            if (items.some(d => d.status === 'Processing' || d.status === 'Pending'))
                pollTimerRef.current = setTimeout(() => fetchDocs(true), 3000);
        } catch (err) { if (!silent) showApiError(toast, err, 'Belgeler yüklenemedi.'); }
        finally { if (!silent) setDocsLoading(false); }
    }, [toast]);

    const goToPage = useCallback((p) => {
        pageRef.current = p;
        setPage(p);
        fetchDocs(false);
    }, [fetchDocs]);

    const handleDocSearch = useCallback((value) => {
        setDocSearch(value);
        searchRef.current = value;
        if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
        searchTimerRef.current = setTimeout(() => {
            pageRef.current = 1;   // arama değişince ilk sayfaya dön
            setPage(1);
            fetchDocs(false);
        }, 300);
    }, [fetchDocs]);

    const deleteDoc = useCallback(async (id) => {
        setDocuments(prev => prev.filter(d => d.id !== id));
        try {
            await deleteDocument(id);
            fetchDocs(true);  // toplam sayaç + sayfa dolgusunu reconcile et
        } catch (err) {
            fetchDocs(true);
            throw err;
        }
    }, [fetchDocs]);

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
    }, [documents, toast, fetchDocs]);


    const uploadFile = useCallback(async (file) => {
        const uid = Date.now() + Math.random();
        setUploads(prev => [...prev, { id: uid, name: file.name, progress: 0, status: 'uploading' }]);
        try {
            await uploadDocument(file, uploadDepartmentId, (p) => setUploads(prev => prev.map(u => u.id === uid ? { ...u, progress: p } : u)));
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
    }, [toast, fetchDocs, uploadDepartmentId]);

    const processFiles = useCallback((files) => {
        // Departman zorunlu — seçilmeden yükleme yapılmaz (backend de reddeder).
        if (!uploadDepartmentId) {
            toast.error('Önce belgelerin yükleneceği departmanı seçin.');
            return;
        }

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

        // Throttled upload — 5'li gruplar: bir grup tamamen bitmeden sonraki başlamaz.
        // Sebep: backend upload rate limit (10/dk) + Mistral OCR rate limit + browser network sıkışması.
        (async () => {
            const BATCH = 5;
            for (let i = 0; i < valid.length; i += BATCH) {
                const batch = valid.slice(i, i + BATCH);
                await Promise.all(batch.map(uploadFile));
            }
        })();
    }, [toast, uploadFile, uploadDepartmentId]);

    return {
        documents, setDocuments,
        docsLoading,
        uploads,
        docSearch, setDocSearch: handleDocSearch,
        dragOver, setDragOver,
        uploadDepartmentId, setUploadDepartmentId,
        // pagination
        page, totalCount, grandTotal, pageSize: PAGE_SIZE, goToPage,
        fetchDocs,
        deleteDoc,
        batchDeleteDocs,
        batchReprocessDocs,
        batchDownloadDocs,
        uploadFile,
        processFiles,
    };
}
