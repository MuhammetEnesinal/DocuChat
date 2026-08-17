import { useState, useCallback, useRef, useEffect } from 'react';
import { getDepartments, createDepartment, updateDepartment, deleteDepartment, deleteDepartmentsBatch } from '../services/api';
import { useToast } from '../components/shared/Toast';
import { showApiError } from '../lib/format';
import { useRealtimeRefresh } from './useRealtime';
import { RealtimeEvents } from '../lib/realtimeEvents';

const PAGE_SIZE = 20;

// İKİ veri kaynağı:
//  - departments: sayfalı YÖNETİM listesi (arama + pagination) → DepartmentManager
//  - allDepartments: TAM liste → seçiciler (kullanıcı modalı, belge yükleme). Sayfalı liste
//    seçicilere yetmez; admin herhangi bir departmanı atayabilmeli. (users deseninde kullanıcı
//    seçici olmadığı için orada tek liste yeter; departman seçici olduğundan burada iki liste.)
export function useDepartments() {
    // ── Sayfalı yönetim listesi ──
    const [departments, setDepartments] = useState([]);
    const [loading, setLoading] = useState(true);
    const [page, setPage] = useState(1);
    const [totalCount, setTotalCount] = useState(0);
    const [search, setSearch] = useState('');
    const pageRef = useRef(1);
    const searchRef = useRef('');
    const searchTimerRef = useRef(null);

    // ── Tam liste (seçiciler) ──
    const [allDepartments, setAllDepartments] = useState([]);

    const toast = useToast();

    useEffect(() => {
        return () => { if (searchTimerRef.current) clearTimeout(searchTimerRef.current); };
    }, []);

    // Sayfalı yönetim listesini çeker.
    const fetchDepartments = useCallback(async (silent = false) => {
        if (!silent) setLoading(true);
        try {
            const params = { page: pageRef.current, pageSize: PAGE_SIZE };
            if (searchRef.current) params.search = searchRef.current;
            const res = await getDepartments(params);
            const data = res.data.data || {};
            const items = data.items || [];
            const total = data.totalCount ?? items.length;

            // Sayfa aralık dışında kaldıysa (son sayfadaki son departman silindi) düzelt.
            const lastPage = Math.max(1, Math.ceil(total / PAGE_SIZE));
            if (pageRef.current > lastPage) {
                pageRef.current = lastPage;
                setPage(lastPage);
                return fetchDepartments(silent);
            }

            setDepartments(items);
            setTotalCount(total);
        } catch (err) {
            if (!silent) showApiError(toast, err, 'Departmanlar yüklenemedi.');
        } finally {
            if (!silent) setLoading(false);
        }
    }, [toast]);

    // Tam listeyi çeker (params yok → backend tümünü döner). Seçiciler + tab rozeti sayısı için.
    const fetchAllDepartments = useCallback(async () => {
        try {
            const res = await getDepartments();
            setAllDepartments(res.data.data || []);
        } catch { /* sessiz — seçici boş kalır, yönetim listesi hatayı zaten gösterir */ }
    }, []);

    // CRUD sonrası HER İKİ listeyi de tazele (yönetim + seçiciler tutarlı kalsın).
    const refreshBoth = useCallback(() => {
        fetchDepartments(true);
        fetchAllDepartments();
    }, [fetchDepartments, fetchAllDepartments]);

    // Gerçek zamanlı: departman değişince (başka admin CRUD yaptı) tazele. Ayrıca kullanıcı/belge
    // değişimleri de dinlenir — yönetim listesindeki UserCount/DocumentCount sütunları bunlarla değişir.
    // Coalescing 250 ms tüm bu tetikleri tek fetch'e indirir + reconnect telafisi.
    useRealtimeRefresh(
        [RealtimeEvents.DepartmentChanged, RealtimeEvents.UserChanged, RealtimeEvents.DocumentChanged],
        refreshBoth,
    );

    const goToPage = useCallback((p) => {
        pageRef.current = p;
        setPage(p);
        fetchDepartments(false);
    }, [fetchDepartments]);

    const handleSearch = useCallback((value) => {
        setSearch(value);
        searchRef.current = value;
        if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
        searchTimerRef.current = setTimeout(() => {
            pageRef.current = 1;   // arama değişince ilk sayfaya dön
            setPage(1);
            fetchDepartments(false);
        }, 300);
    }, [fetchDepartments]);

    // Hata modal'da gösterilsin diye add/rename YUTULMAZ — modal catch edip basar.
    const addDepartment = useCallback(async (name, code) => {
        await createDepartment(name, code);
        toast.success(`"${name}" departmanı oluşturuldu.`);
        refreshBoth();
    }, [toast, refreshBoth]);

    const renameDepartment = useCallback(async (id, name, code) => {
        await updateDepartment(id, name, code);
        toast.success('Departman güncellendi.');
        refreshBoth();
    }, [toast, refreshBoth]);

    const removeDepartment = useCallback(async (id, name) => {
        try {
            await deleteDepartment(id);
            toast.success(`"${name}" departmanı silindi.`);
            refreshBoth();
        } catch (err) {
            showApiError(toast, err, 'Departman silinemedi.');
            throw err;
        }
    }, [toast, refreshBoth]);

    const batchRemoveDepartments = useCallback(async (ids) => {
        if (ids.length === 0) return 0;
        try {
            const res = await deleteDepartmentsBatch(ids);
            const success = res.data?.data ?? 0;
            const skipped = ids.length - success;
            if (success > 0) toast.success(`${success} departman silindi.`);
            if (skipped > 0) toast.info?.(`${skipped} departman atlandı (bağlı kullanıcı veya belge var).`);
            refreshBoth();
            return success;
        } catch (err) {
            showApiError(toast, err, 'Departmanlar silinemedi.');
            return 0;
        }
    }, [toast, refreshBoth]);

    return {
        // sayfalı yönetim listesi
        departments, loading,
        page, totalCount, pageSize: PAGE_SIZE, goToPage,
        search, setSearch: handleSearch,
        fetchDepartments,
        // tam liste (seçiciler + rozet)
        allDepartments, fetchAllDepartments,
        // CRUD
        addDepartment, renameDepartment, removeDepartment, batchRemoveDepartments,
    };
}
