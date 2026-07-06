import { useState, useCallback, useRef, useEffect } from 'react';
import {
    adminGetUsers, adminCreateUser, adminUpdateUser, adminDeleteUser, adminDeleteUsersBatch,
    adminDownloadBulkImportTemplate, adminBulkImportUsersStream
} from '../services/api';
import { useToast } from '../components/shared/Toast';
import { showApiError, getApiErrorMessage } from '../lib/format';

const PAGE_SIZE = 20;

export function useUsers() {
    const [users, setUsers] = useState([]);
    const [usersLoading, setUsersLoading] = useState(true);
    const [userSearch, setUserSearch] = useState('');
    // Server-side pagination + search
    const [page, setPage] = useState(1);
    const [totalCount, setTotalCount] = useState(0);      // aktif filtreye göre (arama dahil)
    const [grandTotal, setGrandTotal] = useState(0);      // aramasız GENEL toplam — sekme rozeti için
    const pageRef = useRef(1);
    const searchRef = useRef('');
    const searchTimerRef = useRef(null);
    const [showUserModal, setShowUserModal] = useState(false);
    const [editingUser, setEditingUser] = useState(null);
    const [userForm, setUserFormState] = useState({ fullName: '', email: '', personnelCode: '' });
    const [userFormError, setUserFormError] = useState('');
    const [userFormLoading, setUserFormLoading] = useState(false);
    const toast = useToast();

    // Unmount'ta bekleyen arama debounce'u iptal — sayfadan çıkınca hayalet fetch atılmasın.
    useEffect(() => {
        return () => { if (searchTimerRef.current) clearTimeout(searchTimerRef.current); };
    }, []);

    const fetchUsers = useCallback(async (silent = false) => {
        if (!silent) setUsersLoading(true);
        try {
            const params = { page: pageRef.current, pageSize: PAGE_SIZE };
            if (searchRef.current) params.search = searchRef.current;
            const res = await adminGetUsers(params);
            const data = res.data.data || {};
            const items = data.items || [];
            const total = data.totalCount ?? items.length;

            // Geçerli sayfa aralık dışında kaldıysa (son sayfadaki son kullanıcı silindi) düzelt
            const lastPage = Math.max(1, Math.ceil(total / PAGE_SIZE));
            if (pageRef.current > lastPage) {
                pageRef.current = lastPage;
                setPage(lastPage);
                return fetchUsers(silent);
            }

            setUsers(items);
            setTotalCount(total);
            if (!searchRef.current) setGrandTotal(total);  // arama yokken gelen total = genel toplam
        } catch (err) { if (!silent) showApiError(toast, err, 'Kullanıcılar yüklenemedi.'); }
        finally { if (!silent) setUsersLoading(false); }
    }, [toast]);

    const goToUsersPage = useCallback((p) => {
        pageRef.current = p;
        setPage(p);
        fetchUsers(false);
    }, [fetchUsers]);

    const handleUserSearch = useCallback((value) => {
        setUserSearch(value);
        searchRef.current = value;
        if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
        searchTimerRef.current = setTimeout(() => {
            pageRef.current = 1;   // arama değişince ilk sayfaya dön
            setPage(1);
            fetchUsers(false);
        }, 300);
    }, [fetchUsers]);

    const setUserForm = useCallback((key, val) => {
        setUserFormState(prev => ({ ...prev, [key]: val }));
    }, []);

    const openAddModal = useCallback(() => {
        setEditingUser(null);
        setUserFormState({ fullName: '', email: '', personnelCode: '' });
        setUserFormError('');
        setShowUserModal(true);
    }, []);

    const openEditModal = useCallback((u) => {
        setEditingUser(u);
        setUserFormState({ fullName: u.fullName, email: u.email, personnelCode: u.personnelCode || '' });
        setUserFormError('');
        setShowUserModal(true);
    }, []);

    const closeUserModal = useCallback(() => {
        setShowUserModal(false);
        setEditingUser(null);
    }, []);

    const handleSubmitUser = useCallback(async (e) => {
        e.preventDefault();
        setUserFormError('');
        setUserFormLoading(true);
        try {
            if (editingUser) {
                await adminUpdateUser(editingUser.id, userForm.fullName, userForm.email, userForm.personnelCode);
                toast.success(`"${userForm.fullName}" güncellendi.`);
            } else {
                await adminCreateUser(userForm.fullName, userForm.email, userForm.personnelCode);
                toast.success(`"${userForm.fullName}" oluşturuldu.`);
            }
            closeUserModal();
            fetchUsers(true);
        } catch (err) {
            const errorMsg = getApiErrorMessage(err, editingUser ? 'Kullanıcı güncellenemedi.' : 'Kullanıcı oluşturulamadı.');
            setUserFormError(errorMsg);
            showApiError(toast, err, errorMsg);
        } finally { setUserFormLoading(false); }
    }, [editingUser, userForm, toast, closeUserModal, fetchUsers]);

    const deleteUser = useCallback(async (id) => {
        const snapshot = [...users];
        setUsers(prev => prev.filter(u => u.id !== id));
        try {
            await adminDeleteUser(id);
            fetchUsers(true);  // toplam sayaç + sayfa dolgusunu reconcile et
        } catch (err) {
            setUsers(snapshot);
            throw err;
        }
    }, [users, fetchUsers]);

    // Çoklu kullanıcı silme — TEK HTTP isteği (batch-delete). Admin role'üne sahip kullanıcılar
    // serverda atlanır; serverdan dönen sayı kadar başarılı.
    const batchDeleteUsers = useCallback(async (ids) => {
        if (ids.length === 0) return 0;
        const snapshot = users;
        // Optimistic: tümünü kaldır; hata olursa snapshot'tan geri yükle
        setUsers(prev => prev.filter(u => !ids.includes(u.id)));
        try {
            const res = await adminDeleteUsersBatch(ids);
            const success = res.data?.data ?? 0;
            const skipped = ids.length - success;
            if (success > 0) toast.success(`${success} kullanıcı silindi.`);
            if (skipped > 0) toast.info?.(`${skipped} kullanıcı atlandı (admin veya bulunamadı).`);
            fetchUsers(true);
            return success;
        } catch (err) {
            setUsers(snapshot);
            showApiError(toast, err, 'Toplu silme başarısız.');
            return 0;
        }
    }, [users, toast, fetchUsers]);

    // ── Bulk Import (Excel) ──
    const [showBulkImportModal, setShowBulkImportModal] = useState(false);
    const [bulkImportResult, setBulkImportResult] = useState(null);    // BulkImportUsersSummaryDto | null
    const [bulkImportLoading, setBulkImportLoading] = useState(false);
    // Streaming sırasında progress state — { processed, total, successCount, skippedCount, lastRow }
    const [bulkImportProgress, setBulkImportProgress] = useState(null);

    const openBulkImportModal = useCallback(() => {
        setShowBulkImportModal(true);
        setBulkImportResult(null);
        setBulkImportProgress(null);
    }, []);
    const closeBulkImportModal = useCallback(() => {
        setShowBulkImportModal(false);
        setBulkImportResult(null);
        setBulkImportProgress(null);
    }, []);

    const handleDownloadTemplate = useCallback(async () => {
        try {
            const res = await adminDownloadBulkImportTemplate();
            const url = URL.createObjectURL(res.data);
            const a = document.createElement('a');
            a.href = url;
            a.download = 'kullanici-toplu-yukleme-sablonu.xlsx';
            document.body.appendChild(a);
            a.click();
            a.remove();
            URL.revokeObjectURL(url);
        } catch (err) {
            showApiError(toast, err, 'Şablon indirilemedi.');
        }
    }, [toast]);

    const handleBulkImport = useCallback(async (file) => {
        if (!file) return;
        setBulkImportLoading(true);
        setBulkImportResult(null);
        // İlk progress state — start event gelmeden modal'da spinner gözüksün
        setBulkImportProgress({ processed: 0, total: 0, successCount: 0, skippedCount: 0 });

        // Local running totals — state setter'lar async olduğu için event içinde önceki değeri
        // güvenilir okuyamayız; lokal sayaçlardan increment ederiz.
        let runningSuccess = 0;
        let runningSkipped = 0;

        const res = await adminBulkImportUsersStream(file, (evt) => {
            if (evt.type === 'start') {
                setBulkImportProgress({
                    processed: 0,
                    total: evt.total ?? 0,
                    successCount: 0,
                    skippedCount: 0,
                });
            } else if (evt.type === 'progress') {
                if (evt.status === 'success') runningSuccess++;
                else runningSkipped++;
                setBulkImportProgress({
                    processed: evt.processed ?? 0,
                    total: evt.total ?? 0,
                    successCount: runningSuccess,
                    skippedCount: runningSkipped,
                    lastRow: evt.row,
                    lastEmail: evt.email,
                    lastStatus: evt.status,
                });
            }
            // done & error son adımda handle edilir
        });

        setBulkImportLoading(false);

        if (res.aborted) {
            setBulkImportProgress(null);
            return;
        }
        if (!res.ok) {
            setBulkImportProgress(null);
            toast.error?.(res.error || 'Toplu yükleme başarısız.');
            return;
        }

        const summary = res.summary;
        setBulkImportResult(summary);
        setBulkImportProgress(null);

        if (summary?.successCount > 0) {
            fetchUsers(true);
        }
        const success = summary?.successCount ?? 0;
        const skipped = summary?.skippedCount ?? 0;
        if (success > 0) toast.success(`${success} kullanıcı oluşturuldu.`);
        if (skipped > 0) toast.info?.(`${skipped} satır atlandı (detay tabloda).`);
    }, [toast, fetchUsers]);

    return {
        users,
        usersLoading,
        userSearch, setUserSearch: handleUserSearch,
        // pagination
        page, totalCount, grandTotal, pageSize: PAGE_SIZE, goToUsersPage,
        showUserModal,
        editingUser,
        userForm,
        setUserForm,
        userFormError,
        userFormLoading,
        fetchUsers,
        openAddModal,
        openEditModal,
        closeUserModal,
        handleSubmitUser,
        deleteUser,
        batchDeleteUsers,
        // Bulk import
        showBulkImportModal,
        bulkImportResult,
        bulkImportLoading,
        bulkImportProgress,
        openBulkImportModal,
        closeBulkImportModal,
        handleDownloadTemplate,
        handleBulkImport,
    };
}
