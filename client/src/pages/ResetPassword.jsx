import { useState } from 'react';
import { useNavigate, useSearchParams, Link } from 'react-router-dom';
import { resetPassword } from '../services/api';
import { useToast } from '../components/shared/Toast';
import { showApiError, getApiErrorMessage } from '../utils/format';
import AuthCard from '../components/auth/AuthCard';
import FormInput from '../components/auth/FormInput';
import PasswordToggle from '../components/auth/PasswordToggle';
import SubmitButton from '../components/auth/SubmitButton';

export default function ResetPassword() {
    const [searchParams] = useSearchParams();
    const email = searchParams.get('email') || '';
    const token = searchParams.get('token') || '';

    const [newPassword, setNewPassword] = useState('');
    const [confirmPassword, setConfirmPassword] = useState('');
    const [showPassword, setShowPassword] = useState(false);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState('');
    const navigate = useNavigate();
    const toast = useToast();

    if (!email || !token) {
        return (
            <AuthCard>
                <div style={{ textAlign: 'center' }}>
                    <p style={{ color: 'var(--text-muted)', marginBottom: '16px' }}>
                        Geçersiz şifre sıfırlama bağlantısı.
                    </p>
                    <Link to="/forgot-password" style={{ color: 'var(--accent)', fontSize: '14px' }}>
                        Yeni bağlantı iste
                    </Link>
                </div>
            </AuthCard>
        );
    }

    const handleSubmit = async (e) => {
        e.preventDefault();
        setError('');

        if (newPassword !== confirmPassword) {
            setError('Şifreler eşleşmiyor.');
            return;
        }
        if (newPassword.length < 8) {
            setError('Şifre en az 8 karakter olmalıdır.');
            return;
        }

        setLoading(true);
        try {
            await resetPassword(email, token, newPassword);
            toast.success('Şifreniz başarıyla sıfırlandı!');
            navigate('/login');
        } catch (err) {
            const msg = getApiErrorMessage(err, 'Şifre sıfırlanamadı. Token geçersiz veya süresi dolmuş olabilir.');
            setError(msg);
            showApiError(toast, err, msg);
        } finally {
            setLoading(false);
        }
    };

    return (
        <AuthCard>
            <h2 className="text-lg font-semibold text-white mb-2">Yeni Şifre Belirle</h2>
            <p style={{ color: 'var(--text-muted)', fontSize: '14px', marginBottom: '24px' }}>
                {email} için yeni şifrenizi girin.
            </p>

            {error && (
                <div style={{ background: 'rgba(248,113,113,0.1)', border: '1px solid rgba(248,113,113,0.3)',
                    borderRadius: '8px', padding: '10px 14px', color: '#f87171', fontSize: '14px', marginBottom: '16px' }}>
                    {error}
                </div>
            )}

            <form onSubmit={handleSubmit} className="space-y-5">
                <FormInput
                    label="Yeni Şifre"
                    type={showPassword ? 'text' : 'password'}
                    value={newPassword}
                    onChange={(e) => setNewPassword(e.target.value)}
                    placeholder="••••••••"
                    required
                    suffix={<PasswordToggle show={showPassword} onToggle={() => setShowPassword(v => !v)} />}
                />

                <FormInput
                    label="Şifreyi Tekrarla"
                    type="password"
                    value={confirmPassword}
                    onChange={(e) => setConfirmPassword(e.target.value)}
                    placeholder="••••••••"
                    required
                />

                <SubmitButton loading={loading}>
                    {loading ? 'Kaydediliyor...' : 'Şifremi Sıfırla'}
                </SubmitButton>
            </form>
        </AuthCard>
    );
}
