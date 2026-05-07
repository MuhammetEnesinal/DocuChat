import { useState } from 'react';
import { useNavigate, useSearchParams, Link } from 'react-router-dom';
import { resetPassword } from '../services/api';
import { useToast } from '../components/shared/Toast';
import Logo from '../components/shared/Logo';
import FormInput from '../components/auth/FormInput';

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

    const EyeIcon = () => (
        <button type="button" onClick={() => setShowPassword(!showPassword)}
            style={{ color: '#64748b', background: 'none', border: 'none', cursor: 'pointer' }}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                {showPassword
                    ? <><path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94" /><path d="M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19" /><line x1="1" y1="1" x2="23" y2="23" /></>
                    : <><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" /><circle cx="12" cy="12" r="3" /></>
                }
            </svg>
        </button>
    );

    if (!email || !token) {
        return (
            <div className="min-h-screen flex items-center justify-center" style={{ background: 'var(--navy)' }}>
                <div className="w-full max-w-md px-4">
                    <div className="rounded-2xl p-8 text-center" style={{ background: 'var(--surface)', border: '1px solid var(--border)' }}>
                        <p style={{ color: 'var(--text-muted)', marginBottom: '16px' }}>
                            Geçersiz şifre sıfırlama bağlantısı.
                        </p>
                        <Link to="/forgot-password" style={{ color: 'var(--accent)', fontSize: '14px' }}>
                            Yeni bağlantı iste
                        </Link>
                    </div>
                </div>
            </div>
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
            const msg = err.response?.data?.error?.message || 'Şifre sıfırlanamadı. Token geçersiz veya süresi dolmuş olabilir.';
            setError(msg);
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="min-h-screen flex items-center justify-center" style={{ background: 'var(--navy)' }}>
            <div className="w-full max-w-md px-4">
                <div className="text-center mb-10">
                    <Logo size="md" />
                </div>

                <div className="rounded-2xl p-8" style={{ background: 'var(--surface)', border: '1px solid var(--border)' }}>
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
                            suffix={<EyeIcon />}
                        />

                        <FormInput
                            label="Şifreyi Tekrarla"
                            type={showPassword ? 'text' : 'password'}
                            value={confirmPassword}
                            onChange={(e) => setConfirmPassword(e.target.value)}
                            placeholder="••••••••"
                            required
                        />

                        <button type="submit" disabled={loading}
                            className="w-full py-3 rounded-xl font-semibold text-white transition-all"
                            style={{ background: loading ? 'var(--navy-light)' : 'var(--accent)', cursor: loading ? 'not-allowed' : 'pointer' }}>
                            {loading ? 'Kaydediliyor...' : 'Şifremi Sıfırla'}
                        </button>
                    </form>
                </div>
            </div>
        </div>
    );
}
