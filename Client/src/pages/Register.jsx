import { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { register } from '../services/api';
import { useAuth } from '../hooks/useAuth';
import { useToast } from '../components/shared/Toast';
import AuthCard from '../components/auth/AuthCard';
import FormInput from '../components/auth/FormInput';
import ErrorAlert from '../components/auth/ErrorAlert';
import PasswordToggle from '../components/auth/PasswordToggle';
import SubmitButton from '../components/auth/SubmitButton';

export default function Register() {
    const [fullName, setFullName] = useState('');
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [confirmPassword, setConfirmPassword] = useState('');
    const [showPassword, setShowPassword] = useState(false);
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(false);
    const { setAuth } = useAuth();
    const navigate = useNavigate();
    const toast = useToast();

    const handleSubmit = async (e) => {
        e.preventDefault();
        setError('');

        if (password !== confirmPassword) {
            setError('Şifreler eşleşmiyor.');
            return;
        }
        setLoading(true);
        try {
            const res = await register(fullName, email, password);
            const { token, ...user } = res.data.data;
            setAuth(token, user);
            toast.success(`Hesabınız oluşturuldu. Hoş geldiniz, ${user.fullName}!`);
            navigate(user.roles?.includes('Admin') ? '/admin' : '/chat');
        } catch (err) {
            const data = err.response?.data;
            const msg = data?.error?.message;
            const errors = data?.error?.errors;
            const errorMsg = errors?.join(' ') || msg || 'Kayıt başarısız.';
            setError(errorMsg);
            toast.error(errorMsg);
        } finally {
            setLoading(false);
        }
    };

    const passwordMismatch = confirmPassword && confirmPassword !== password;

    return (
        <AuthCard>
            <h2 className="text-lg font-semibold text-white mb-6">Hesap Oluştur</h2>
            <ErrorAlert message={error} />

            <form onSubmit={handleSubmit} className="space-y-4">
                <FormInput label="Ad Soyad" value={fullName}
                    onChange={(e) => setFullName(e.target.value)}
                    placeholder="Ad Soyad" required />

                <FormInput label="E-posta" type="email" value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="ornek@sirket.com" required />

                <FormInput label="Şifre" type={showPassword ? 'text' : 'password'}
                    value={password} onChange={(e) => setPassword(e.target.value)}
                    placeholder="En az 8 karakter" required
                    suffix={<PasswordToggle show={showPassword} onToggle={() => setShowPassword(v => !v)} />}
                    hint="Büyük/küçük harf, rakam ve özel karakter içermeli" />

                <FormInput label="Şifre Tekrar" type={showPassword ? 'text' : 'password'}
                    value={confirmPassword} onChange={(e) => setConfirmPassword(e.target.value)}
                    placeholder="Şifreyi tekrar girin" required
                    error={passwordMismatch ? 'Şifreler eşleşmiyor' : ''} />

                <SubmitButton loading={loading}>
                    {loading ? 'Kayıt yapılıyor...' : 'Hesap Oluştur'}
                </SubmitButton>
            </form>

            <p className="text-center text-sm mt-5" style={{ color: '#64748b' }}>
                Zaten hesabın var mı?{' '}
                <Link to="/login" style={{ color: 'var(--accent-light)', textDecoration: 'none' }}
                    onMouseEnter={(e) => e.currentTarget.style.textDecoration = 'underline'}
                    onMouseLeave={(e) => e.currentTarget.style.textDecoration = 'none'}>
                    Giriş Yap
                </Link>
            </p>
        </AuthCard>
    );
}
