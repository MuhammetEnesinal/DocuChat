import { useState, useCallback } from 'react';
import { getDepartments, createDepartment, updateDepartment, deleteDepartment, deleteDepartmentsBatch } from '../services/api';
import { useToast } from '../components/shared/Toast';
import { showApiError } from '../lib/format';

// Departman yönetimi (admin) + selector'lar için departman listesi.
export function useDepartments() {
    const [departments, setDepartments] = useState([]);
    const [loading, setLoading] = useState(true);
    const toast = useToast();

    const fetchDepartments = useCallback(async (silent = false) => {
        if (!silent) setLoading(true);
        try {
            const res = await getDepartments();
            setDepartments(res.data.data || []);
        } catch (err) {
            if (!silent) showApiError(toast, err, 'Departmanlar yüklenemedi.');
        } finally {
            if (!silent) setLoading(false);
        }
    }, [toast]);

    // Hata modal'da gösterilsin diye YUTULMAZ — modal catch edip mesajı basar.
    const addDepartment = useCallback(async (name, code) => {
        await createDepartment(name, code);
        toast.success(`"${name}" departmanı oluşturuldu.`);
        fetchDepartments(true);
    }, [toast, fetchDepartments]);

    const renameDepartment = useCallback(async (id, name, code) => {
        await updateDepartment(id, name, code);
        toast.success('Departman güncellendi.');
        fetchDepartments(true);
    }, [toast, fetchDepartments]);

    const removeDepartment = useCallback(async (id, name) => {
        try {
            await deleteDepartment(id);
            toast.success(`"${name}" departmanı silindi.`);
            fetchDepartments(true);
        } catch (err) {
            showApiError(toast, err, 'Departman silinemedi.');
            throw err;
        }
    }, [toast, fetchDepartments]);

    // Çoklu silme — tek istek. Server bağlı kullanıcı/belgesi olanları atlar; dönen sayı
    // gerçekten silinendir, aradaki fark kullanıcıya bildirilir.
    const batchRemoveDepartments = useCallback(async (ids) => {
        if (ids.length === 0) return 0;
        try {
            const res = await deleteDepartmentsBatch(ids);
            const success = res.data?.data ?? 0;
            const skipped = ids.length - success;
            if (success > 0) toast.success(`${success} departman silindi.`);
            if (skipped > 0) toast.info?.(`${skipped} departman atlandı (bağlı kullanıcı veya belge var).`);
            fetchDepartments(true);
            return success;
        } catch (err) {
            showApiError(toast, err, 'Departmanlar silinemedi.');
            return 0;
        }
    }, [toast, fetchDepartments]);

    return { departments, loading, fetchDepartments, addDepartment, renameDepartment, removeDepartment, batchRemoveDepartments };
}
