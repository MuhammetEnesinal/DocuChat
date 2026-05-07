import { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { login } from '../services/api';
import { useAuth } from '../hooks/useAuth';
import { useToast } from '../components/shared/Toast';
import Logo from '../components/shared/Logo';
import FormInput from '../components/auth/FormInput';
import ErrorAlert from '../components/auth/ErrorAlert';

export default function Login() {
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [showPassword, setShowPassword] = useState(false);
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(false);
    const { setAuth } = useAuth();
    const navigate = useNavigate();
    const toast = useToast();

    const handleSubmit = async (e) => {
        e.preventDefault();
        setError('');
        setLoading(true);
        try {
            const res = await login(email, password);
            const { token, ...user } = res.data.data;
            setAuth(token, user);
            toast.success(`Hoş geldiniz, ${user.fullName}!`);
            navigate(user.roles?.includes('Admin') ? '/admin' : '/chat');
        } catch (err) {
            const status = err.response?.status;
            const data = err.response?.data;
            const msg = status === 429
                ? (data?.message || 'Çok fazla giriş denemesi. Lütfen 1 dakika bekleyin.')
                : (data?.error?.message || data?.message || 'Giriş başarısız.');
            setError(msg);
            toast.error(msg);
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
                    : <><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" /><circle cx="12" cy="12" r="3" /></>
                }
            </svg>
        </button>
    );

    return (
        <div className="min-h-screen flex items-center justify-center" style={{ background: 'var(--navy)' }}>
            <div className="w-full max-w-md px-4">
                <div className="text-center mb-10">
                    <Logo size="md" />
                </div>

                <div className="rounded-2xl p-8" style={{ background: 'var(--surface)', border: '1px solid var(--border)' }}>
                    <h2 className="text-lg font-semibold text-white mb-6">Hesabınıza giriş yapın</h2>
                    <ErrorAlert message={error} />

                    <form onSubmit={handleSubmit} className="space-y-5">
                        <FormInput label="E-posta" type="email" value={email}
                            onChange={(e) => setEmail(e.target.value)}
                            placeholder="ornek@sirket.com" required />

                        <div>
                            <FormInput label="Şifre" type={showPassword ? 'text' : 'password'}
                                value={password} onChange={(e) => setPassword(e.target.value)}
                                placeholder="••••••••" required suffix={<EyeIcon />} />
                            <div style={{ textAlign: 'right', marginTop: '6px' }}>
                                <Link to="/forgot-password"
                                    style={{ fontSize: '12px', color: 'var(--accent)', textDecoration: 'none' }}>
                                    Şifremi Unuttum
                                </Link>
                            </div>
                        </div>

                        <button type="submit" disabled={loading}
                            className="w-full py-3 rounded-xl font-semibold text-white transition-all"
                            style={{ background: loading ? 'var(--navy-light)' : 'var(--accent)', cursor: loading ? 'not-allowed' : 'pointer' }}>
                            {loading ? 'Giriş yapılıyor...' : 'Giriş Yap'}
                        </button>
                    </form>


                </div>
            </div>
        </div>
    );
}