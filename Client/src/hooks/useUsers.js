import { useState, useCallback } from 'react';
import { adminGetUsers, adminCreateUser, adminUpdateUser, adminDeleteUser } from '../services/api';
import { useToast } from '../components/shared/Toast';

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
        } catch { toast.error('Kullanıcılar yüklenemedi.'); }
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
            const msg = err.response?.data?.error?.message;
            const errors = err.response?.data?.error?.errors;
            const errorMsg = errors?.join(' ') || msg || (editingUser ? 'Kullanıcı güncellenemedi.' : 'Kullanıcı oluşturulamadı.');
            setUserFormError(errorMsg);
            toast.error(errorMsg);
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
    };
}
