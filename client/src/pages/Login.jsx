import { useState, useEffect } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { login } from '../services/api';
import { showApiError, getApiErrorMessage } from '../lib/format';
import { useAuth } from '../hooks/useAuth';
import { useToast } from '../components/shared/Toast';
import AuthCard from '../components/auth/AuthCard';
import FormInput from '../components/auth/FormInput';
import ErrorAlert from '../components/auth/ErrorAlert';
import PasswordToggle from '../components/auth/PasswordToggle';
import SubmitButton from '../components/auth/SubmitButton';

export default function Login() {
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [showPassword, setShowPassword] = useState(false);
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(false);
    const { setAuth } = useAuth();
    const navigate = useNavigate();
    const toast = useToast();

    useEffect(() => {
        if (sessionStorage.getItem('session_expired')) {
            sessionStorage.removeItem('session_expired');
            toast.warning('Oturumunuz sona erdi. Lütfen tekrar giriş yapın.');
        }
    }, [toast]);

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
            const msg = getApiErrorMessage(err, 'Giriş başarısız.');
            setError(msg);
            showApiError(toast, err, msg);
        } finally {
            setLoading(false);
        }
    };

    return (
        <AuthCard>
            <h2 className="text-lg font-semibold mb-6" style={{ color: '#f1f5f9' }}>Hesabınıza giriş yapın</h2>
            <ErrorAlert message={error} />

            <form onSubmit={handleSubmit} className="space-y-5">
                <FormInput label="E-posta" type="email" value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="ornek@sirket.com" required />

                <div>
                    <FormInput label="Şifre" type={showPassword ? 'text' : 'password'}
                        value={password} onChange={(e) => setPassword(e.target.value)}
                        placeholder="••••••••" required
                        suffix={<PasswordToggle show={showPassword} onToggle={() => setShowPassword(v => !v)} />} />
                    <div style={{ textAlign: 'right', marginTop: '6px' }}>
                        <Link to="/forgot-password"
                            style={{ fontSize: '12px', color: 'var(--accent)', textDecoration: 'none' }}>
                            Şifremi Unuttum
                        </Link>
                    </div>
                </div>

                <SubmitButton loading={loading}>
                    {loading ? 'Giriş yapılıyor...' : 'Giriş Yap'}
                </SubmitButton>
            </form>
        </AuthCard>
    );
}
