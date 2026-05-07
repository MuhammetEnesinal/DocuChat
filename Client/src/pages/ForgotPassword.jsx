import { useState } from 'react';
import { Link } from 'react-router-dom';
import { forgotPassword } from '../services/api';
import { useToast } from '../components/shared/Toast';
import Logo from '../components/shared/Logo';
import FormInput from '../components/auth/FormInput';

export default function ForgotPassword() {
    const [email, setEmail] = useState('');
    const [loading, setLoading] = useState(false);
    const [sent, setSent] = useState(false);
    const toast = useToast();

    const handleSubmit = async (e) => {
        e.preventDefault();
        setLoading(true);
        try {
            await forgotPassword(email);
            setSent(true);
        } catch {
            toast.error('Bir hata oluştu. Lütfen tekrar deneyin.');
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
                    {sent ? (
                        <div style={{ textAlign: 'center' }}>
                            <div style={{ fontSize: '48px', marginBottom: '16px' }}>📧</div>
                            <h2 className="text-lg font-semibold text-white mb-3">E-posta Gönderildi</h2>
                            <p style={{ color: 'var(--text-muted)', fontSize: '14px', marginBottom: '24px' }}>
                                Şifre sıfırlama bağlantısı e-posta adresinize gönderildi.
                                Lütfen gelen kutunuzu kontrol edin.
                            </p>
                            <Link to="/login"
                                style={{ color: 'var(--accent)', fontSize: '14px', textDecoration: 'none' }}>
                                Giriş sayfasına dön
                            </Link>
                        </div>
                    ) : (
                        <>
                            <h2 className="text-lg font-semibold text-white mb-2">Şifremi Unuttum</h2>
                            <p style={{ color: 'var(--text-muted)', fontSize: '14px', marginBottom: '24px' }}>
                                E-posta adresinizi girin, şifre sıfırlama bağlantısı gönderelim.
                            </p>

                            <form onSubmit={handleSubmit} className="space-y-5">
                                <FormInput
                                    label="E-posta"
                                    type="email"
                                    value={email}
                                    onChange={(e) => setEmail(e.target.value)}
                                    placeholder="ornek@sirket.com"
                                    required
                                />

                                <button type="submit" disabled={loading}
                                    className="w-full py-3 rounded-xl font-semibold text-white transition-all"
                                    style={{ background: loading ? 'var(--navy-light)' : 'var(--accent)', cursor: loading ? 'not-allowed' : 'pointer' }}>
                                    {loading ? 'Gönderiliyor...' : 'Sıfırlama Bağlantısı Gönder'}
                                </button>
                            </form>

                            <div style={{ textAlign: 'center', marginTop: '20px' }}>
                                <Link to="/login"
                                    style={{ color: 'var(--text-muted)', fontSize: '13px', textDecoration: 'none' }}>
                                    ← Giriş sayfasına dön
                                </Link>
                            </div>
                        </>
                    )}
                </div>
            </div>
        </div>
    );
}
