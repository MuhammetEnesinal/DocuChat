import { useState, useCallback } from 'react';
import { adminGetUsers, adminCreateUser, adminUpdateUser, adminDeleteUser, adminDeleteUsersBatch } from '../services/api';
import { useToast } from '../components/shared/Toast';
import { showApiError, getApiErrorMessage } from '../utils/format';

export function useUsers() {
    const [users, setUsers] = useState([]);
    const [usersLoading, setUsersLoading] = useState(true);
    const [userSearch, setUserSearch] = useState('');
    const [showUserModal, setShowUserModal] = useState(false);
    const [editingUser, setEditingUser] = useState(null);
    const [userForm, setUserFormState] = useState({ fullName: '', email: '', password: '' });
    const [userFormError, setUserFormError] = useState('');
    const [userFormLoading, setUserFormLoading] = useState(false);
    const toast = useToast();

    const fetchUsers = useCallback(async () => {
        setUsersLoading(true);
        try {
            const res = await adminGetUsers();
            setUsers(res.data.data || []);
        } catch (err) { showApiError(toast, err, 'Kullanıcılar yüklenemedi.'); }
        finally { setUsersLoading(false); }
    }, [toast]);

    const setUserForm = useCallback((key, val) => {
        setUserFormState(prev => ({ ...prev, [key]: val }));
    }, []);

    const openAddModal = useCallback(() => {
        setEditingUser(null);
        setUserFormState({ fullName: '', email: '', password: '' });
        setUserFormError('');
        setShowUserModal(true);
    }, []);

    const openEditModal = useCallback((u) => {
        setEditingUser(u);
        setUserFormState({ fullName: u.fullName, email: u.email, password: '' });
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
                await adminUpdateUser(editingUser.id, userForm.fullName, userForm.email, userForm.password);
                toast.success(`"${userForm.fullName}" güncellendi.`);
            } else {
                await adminCreateUser(userForm.fullName, userForm.email, userForm.password);
                toast.success(`"${userForm.fullName}" oluşturuldu.`);
            }
            closeUserModal();
            fetchUsers();
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
        } catch (err) {
            setUsers(snapshot);
            throw err;
        }
    }, [users]);

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
            await fetchUsers();
            return success;
        } catch (err) {
            setUsers(snapshot);
            showApiError(toast, err, 'Toplu silme başarısız.');
            return 0;
        }
    }, [users, toast, fetchUsers]);

    return {
        users,
        usersLoading,
        userSearch, setUserSearch,
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
    };
}
