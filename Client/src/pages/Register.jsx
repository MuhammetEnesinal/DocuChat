import { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { register } from '../services/api';
import useAuthStore from '../store/authStore';
import { useToast } from '../components/Toast';

export default function Register() {
    const [fullName, setFullName] = useState('');
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [confirmPassword, setConfirmPassword] = useState('');
    const [showPassword, setShowPassword] = useState(false);
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(false);
    const { setAuth } = useAuthStore();
    const navigate = useNavigate();
    const toast = useToast();

    const handleSubmit = async (e) => {
        e.preventDefault();
        setError('');

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
            const msg = err.response?.data?.error?.message;
            const errors = err.response?.data?.error?.errors;
            const errorText = errors?.join(' ') || msg || 'Kayıt başarısız.';
            setError(errorText);
            toast.error(errorText);
        } finally {
            setLoading(false);
        }
    };

    const inputStyle = {
        background: 'var(--surface2)', border: '1px solid var(--border)',
        fontSize: '0.9rem', width: '100%',
    };

    return (
        <div className="min-h-screen flex items-center justify-center" style={{ background: 'var(--navy)' }}>
            <div className="w-full max-w-md px-4">
                <div className="text-center mb-10">
                    <div className="inline-flex items-center justify-center w-14 h-14 rounded-2xl mb-5"
                        style={{ background: 'var(--accent)', boxShadow: '0 0 30px rgba(59,130,246,0.3)' }}>
                        <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2">
                            <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                        </svg>
                    </div>
                    <h1 className="text-3xl font-bold text-white tracking-tight">DocuChat</h1>
                    <p className="text-sm mt-2" style={{ color: 'var(--gray-light)' }}>Kurumsal Doküman Asistanı</p>
                </div>

                <div className="rounded-2xl p-8" style={{ background: 'var(--surface)', border: '1px solid var(--border)' }}>
                    <h2 className="text-lg font-semibold text-white mb-6">Hesap Oluştur</h2>

                    {error && (
                        <div className="mb-4 p-3 rounded-lg text-sm"
                            style={{ background: 'rgba(239,68,68,0.1)', border: '1px solid rgba(239,68,68,0.3)', color: '#fca5a5' }}>
                            {error}
                        </div>
                    )}

                    <form onSubmit={handleSubmit} className="space-y-4">
                        <div>
                            <label className="block text-sm font-medium mb-2" style={{ color: '#94a3b8' }}>Ad Soyad</label>
                            <input type="text" value={fullName} onChange={(e) => setFullName(e.target.value)}
                                required placeholder="Ad Soyad"
                                className="px-4 py-3 rounded-xl text-white placeholder-slate-500 outline-none transition-all"
                                style={inputStyle}
                                onFocus={(e) => e.target.style.borderColor = 'var(--accent)'}
                                onBlur={(e) => e.target.style.borderColor = 'var(--border)'} />
                        </div>

                        <div>
                            <label className="block text-sm font-medium mb-2" style={{ color: '#94a3b8' }}>E-posta</label>
                            <input type="email" value={email} onChange={(e) => setEmail(e.target.value)}
                                required placeholder="ornek@sirket.com"
                                className="px-4 py-3 rounded-xl text-white placeholder-slate-500 outline-none transition-all"
                                style={inputStyle}
                                onFocus={(e) => e.target.style.borderColor = 'var(--accent)'}
                                onBlur={(e) => e.target.style.borderColor = 'var(--border)'} />
                        </div>

                        <div>
                            <label className="block text-sm font-medium mb-2" style={{ color: '#94a3b8' }}>Şifre</label>
                            <div className="relative">
                                <input type={showPassword ? 'text' : 'password'} value={password}
                                    onChange={(e) => setPassword(e.target.value)}
                                    required placeholder="En az 8 karakter"
                                    className="px-4 py-3 rounded-xl text-white placeholder-slate-500 outline-none transition-all pr-12"
                                    style={inputStyle}
                                    onFocus={(e) => e.target.style.borderColor = 'var(--accent)'}
                                    onBlur={(e) => e.target.style.borderColor = 'var(--border)'} />
                                <button type="button" onClick={() => setShowPassword(!showPassword)}
                                    style={{ position: 'absolute', right: '12px', top: '50%', transform: 'translateY(-50%)', color: '#64748b', background: 'none', border: 'none', cursor: 'pointer' }}>
                                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                        {showPassword
                                            ? <><path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94" /><path d="M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19" /><line x1="1" y1="1" x2="23" y2="23" /></>
                                            : <><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" /><circle cx="12" cy="12" r="3" /></>}
                                    </svg>
                                </button>
                            </div>
                            <p className="text-xs mt-1.5" style={{ color: '#475569' }}>
                                Büyük/küçük harf, rakam ve özel karakter içermeli
                            </p>
                        </div>

                        <div>
                            <label className="block text-sm font-medium mb-2" style={{ color: '#94a3b8' }}>Şifre Tekrar</label>
                            <input type={showPassword ? 'text' : 'password'} value={confirmPassword}
                                onChange={(e) => setConfirmPassword(e.target.value)}
                                required placeholder="Şifreyi tekrar girin"
                                className="px-4 py-3 rounded-xl text-white placeholder-slate-500 outline-none transition-all"
                                style={{
                                    ...inputStyle,
                                    borderColor: confirmPassword && confirmPassword !== password ? 'rgba(248,113,113,0.5)' : 'var(--border)',
                                }}
                                onFocus={(e) => e.target.style.borderColor = confirmPassword !== password ? 'rgba(248,113,113,0.5)' : 'var(--accent)'}
                                onBlur={(e) => e.target.style.borderColor = confirmPassword && confirmPassword !== password ? 'rgba(248,113,113,0.5)' : 'var(--border)'} />
                            {confirmPassword && confirmPassword !== password && (
                                <p className="text-xs mt-1.5" style={{ color: '#f87171' }}>Şifreler eşleşmiyor</p>
                            )}
                        </div>

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