import { useState } from 'react';
import { Link } from 'react-router-dom';
import { forgotPassword } from '../services/api';
import { useToast } from '../components/shared/Toast';
import { showApiError } from '../utils/format';
import AuthCard from '../components/auth/AuthCard';
import FormInput from '../components/auth/FormInput';
import SubmitButton from '../components/auth/SubmitButton';

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
            toast.success('Şifre sıfırlama bağlantısı e-posta adresinize gönderildi.');
            setSent(true);
        } catch (err) {
            showApiError(toast, err, 'Bir hata oluştu. Lütfen tekrar deneyin.');
        } finally {
            setLoading(false);
        }
    };

    return (
        <AuthCard>
            {sent ? (
                <div style={{ textAlign: 'center' }}>
                    <div style={{ fontSize: '48px', marginBottom: '16px' }}>📧</div>
                    <h2 className="text-lg font-semibold text-white mb-3">E-posta Gönderildi</h2>
                    <p style={{ color: 'var(--text-muted)', fontSize: '14px', marginBottom: '24px' }}>
                        Şifre sıfırlama bağlantısı e-posta adresinize gönderildi.
                        Lütfen gelen kutunuzu kontrol edin.
                    </p>
                    <Link to="/login" style={{ color: 'var(--accent)', fontSize: '14px', textDecoration: 'none' }}>
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

                        <SubmitButton loading={loading}>
                            {loading ? 'Gönderiliyor...' : 'Sıfırlama Bağlantısı Gönder'}
                        </SubmitButton>
                    </form>

                    <div style={{ textAlign: 'center', marginTop: '20px' }}>
                        <Link to="/login" style={{ color: 'var(--text-muted)', fontSize: '13px', textDecoration: 'none' }}>
                            ← Giriş sayfasına dön
                        </Link>
                    </div>
                </>
            )}
        </AuthCard>
    );
}
