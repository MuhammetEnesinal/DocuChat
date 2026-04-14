import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { login } from '../services/api';
import useAuthStore from '../store/authStore';

export default function Login() {
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(false);
    const { setAuth } = useAuthStore();
    const navigate = useNavigate();

    const handleSubmit = async (e) => {
        e.preventDefault();
        setError('');
        setLoading(true);
        try {
            const res = await login(email, password);
            const { token, ...user } = res.data.data;
            setAuth(token, user);
            // Role göre yönlendirme
            navigate(user.roles?.includes("Admin") ? "/admin" : "/chat");
        } catch (err) {
            setError(err.response?.data?.error?.message || 'Giriş başarısız.');
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="min-h-screen flex items-center justify-center" style={{ background: 'var(--navy)' }}>
            <div className="w-full max-w-md px-4">
                {/* Logo / Başlık */}
                <div className="text-center mb-10">
                    <div className="inline-flex items-center justify-center w-14 h-14 rounded-2xl mb-5"
                        style={{ background: 'var(--accent)', boxShadow: '0 0 30px rgba(59,130,246,0.3)' }}>
                        <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2">
                            <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                        </svg>
                    </div>
                    <h1 className="text-3xl font-bold text-white tracking-tight">DocuChat</h1>
                    <p className="text-sm mt-2" style={{ color: 'var(--gray-light)' }}>
                        Kurumsal Doküman Asistanı
                    </p>
                </div>

                {/* Form */}
                <div className="rounded-2xl p-8" style={{ background: 'var(--surface)', border: '1px solid var(--border)' }}>
                    <h2 className="text-lg font-semibold text-white mb-6">Hesabınıza giriş yapın</h2>

                    {error && (
                        <div className="mb-4 p-3 rounded-lg text-sm" style={{ background: 'rgba(239,68,68,0.1)', border: '1px solid rgba(239,68,68,0.3)', color: '#fca5a5' }}>
                            {error}
                        </div>
                    )}

                    <form onSubmit={handleSubmit} className="space-y-5">
                        <div>
                            <label className="block text-sm font-medium mb-2" style={{ color: '#94a3b8' }}>
                                E-posta
                            </label>
                            <input
                                type="email"
                                value={email}
                                onChange={(e) => setEmail(e.target.value)}
                                required
                                placeholder="ornek@sirket.com"
                                className="w-full px-4 py-3 rounded-xl text-white placeholder-slate-500 outline-none transition-all"
                                style={{ background: 'var(--surface2)', border: '1px solid var(--border)', fontSize: '0.9rem' }}
                                onFocus={(e) => e.target.style.borderColor = 'var(--accent)'}
                                onBlur={(e) => e.target.style.borderColor = 'var(--border)'}
                            />
                        </div>

                        <div>
                            <label className="block text-sm font-medium mb-2" style={{ color: '#94a3b8' }}>
                                Şifre
                            </label>
                            <input
                                type="password"
                                value={password}
                                onChange={(e) => setPassword(e.target.value)}
                                required
                                placeholder="••••••••"
                                className="w-full px-4 py-3 rounded-xl text-white placeholder-slate-500 outline-none transition-all"
                                style={{ background: 'var(--surface2)', border: '1px solid var(--border)', fontSize: '0.9rem' }}
                                onFocus={(e) => e.target.style.borderColor = 'var(--accent)'}
                                onBlur={(e) => e.target.style.borderColor = 'var(--border)'}
                            />
                        </div>

                        <button
                            type="submit"
                            disabled={loading}
                            className="w-full py-3 rounded-xl font-semibold text-white transition-all"
                            style={{ background: loading ? 'var(--navy-light)' : 'var(--accent)', cursor: loading ? 'not-allowed' : 'pointer' }}
                        >
                            {loading ? 'Giriş yapılıyor...' : 'Giriş Yap'}
                        </button>
                    </form>
                </div>
            </div>
        </div>
    );
}