import { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { register } from '../services/api';
import { useAuth } from '../hooks/useAuth';
import { useToast } from '../components/shared/Toast';
import Logo from '../components/shared/Logo';
import FormInput from '../components/auth/FormInput';
import ErrorAlert from '../components/auth/ErrorAlert';

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

    const validatePassword = (pwd) => {
        const rules = [];
        if (pwd.length < 8) rules.push('En az 8 karakter');
        if (!/[A-Z]/.test(pwd)) rules.push('Büyük harf');
        if (!/[a-z]/.test(pwd)) rules.push('Küçük harf');
        if (!/\d/.test(pwd)) rules.push('Rakam');
        if (!/[^a-zA-Z0-9]/.test(pwd)) rules.push('Özel karakter');
        return rules.length === 0 ? null : 'Şifre şu kuralları içermeli: ' + rules.join(', ');
    };

    const handleSubmit = async (e) => {
        e.preventDefault();
        setError('');
        
        // Şifre geçerliliğini kontrol et
        const passwordError = validatePassword(password);
        if (passwordError) {
            setError(passwordError);
            toast.error(passwordError);
            return;
        }
        
        if (password !== confirmPassword) {
            setError('Şifreler eşleşmiyor.');
            toast.error('Şifreler eşleşmiyor.');
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
            const status = err.response?.status;
            const data = err.response?.data;
            if (status === 429) {
                const errorText = data?.message || 'Çok fazla kayıt denemesi. Lütfen 1 dakika bekleyin.';
                setError(errorText);
                toast.error(errorText);
            } else {
                const msg = data?.error?.message;
                const errors = data?.error?.errors;
                const errorText = errors?.join(' ') || msg || 'Kayıt başarısız.';
                setError(errorText);
                toast.error(errorText);
            }
        } finally {
            setLoading(false);
        }
    };

    const EyeIcon = () => (
        <button type="button" onClick={() => setShowPassword(!showPassword)}
            style={{ color: '#64748b', background: 'none', border: 'none', cursor: 'pointer' }}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                {showPassword
                    ? <><path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94" /><path d="M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19" /><line x1="1" y1="1" x2="23" y2="23" /></>
                    : <><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" /><circle cx="12" cy="12" r="3" /></>}
            </svg>
        </button>
    );

    const passwordMismatch = confirmPassword && confirmPassword !== password;

    return (
        <div className="min-h-screen flex items-center justify-center" style={{ background: 'var(--navy)' }}>
            <div className="w-full max-w-md px-4">
                <div className="text-center mb-10">
                    <Logo size="md" />
                </div>

                <div className="rounded-2xl p-8" style={{ background: 'var(--surface)', border: '1px solid var(--border)' }}>
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
                            placeholder="En az 8 karakter" required suffix={<EyeIcon />}
                            error={password && validatePassword(password)}
                            hint="Büyük/küçük harf, rakam ve özel karakter içermeli" />

                        <FormInput label="Şifre Tekrar" type={showPassword ? 'text' : 'password'}
                            value={confirmPassword} onChange={(e) => setConfirmPassword(e.target.value)}
                            placeholder="Şifreyi tekrar girin" required
                            error={passwordMismatch ? 'Şifreler eşleşmiyor' : ''} />

                        <button type="submit" disabled={loading}
                            className="w-full py-3 rounded-xl font-semibold text-white transition-all mt-2"
                            style={{ background: loading ? 'var(--navy-light)' : 'var(--accent)', cursor: loading ? 'not-allowed' : 'pointer' }}>
                            {loading ? 'Kayıt yapılıyor...' : 'Hesap Oluştur'}
                        </button>
                    </form>

                    <p className="text-center text-sm mt-5" style={{ color: '#64748b' }}>
                        Zaten hesabın var mı?{' '}
                        <Link to="/login" style={{ color: 'var(--accent-light)', textDecoration: 'none' }}
                            onMouseEnter={(e) => e.currentTarget.style.textDecoration = 'underline'}
                            onMouseLeave={(e) => e.currentTarget.style.textDecoration = 'none'}>
                            Giriş Yap
                        </Link>
                    </p>
                </div>
            </div>
        </div>
    );
}