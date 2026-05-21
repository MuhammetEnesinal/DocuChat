import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { motion } from 'framer-motion';
import { getMe, changePassword } from '../services/api';
import { useAuth } from '../hooks/useAuth';
import { useToast } from '../components/shared/Toast';
import { formatDate, showApiError, getApiErrorMessage } from '../utils/format';
import FormInput from '../components/auth/FormInput';
import PasswordToggle from '../components/auth/PasswordToggle';
import ErrorAlert from '../components/auth/ErrorAlert';

function initials(fullName) {
    if (!fullName) return '?';
    return fullName
        .trim()
        .split(/\s+/)
        .map(p => p[0])
        .filter(Boolean)
        .slice(0, 2)
        .join('')
        .toUpperCase();
}

function validatePassword(pwd) {
    if (!pwd) return 'Yeni şifre boş olamaz.';
    if (pwd.length < 8) return 'Şifre en az 8 karakter olmalıdır.';
    if (!/[A-ZÇĞİÖŞÜ]/.test(pwd)) return 'Şifre en az bir büyük harf içermelidir.';
    if (!/[a-zçğıöşü]/.test(pwd)) return 'Şifre en az bir küçük harf içermelidir.';
    if (!/\d/.test(pwd)) return 'Şifre en az bir rakam içermelidir.';
    return null;
}

export default function Profile() {
    const { token, user, setAuth } = useAuth();
    const navigate = useNavigate();
    const toast = useToast();

    const [meLoading, setMeLoading] = useState(true);
    const [currentPwd, setCurrentPwd] = useState('');
    const [newPwd, setNewPwd] = useState('');
    const [confirmPwd, setConfirmPwd] = useState('');
    const [showCurrent, setShowCurrent] = useState(false);
    const [showNew, setShowNew] = useState(false);
    const [showConfirm, setShowConfirm] = useState(false);
    const [formError, setFormError] = useState('');
    const [submitting, setSubmitting] = useState(false);

    useEffect(() => {
        let cancelled = false;
        (async () => {
            try {
                const res = await getMe();
                const fresh = res.data?.data;
                if (!cancelled && fresh) {
                    setAuth(token, {
                        userId: fresh.id,
                        email: fresh.email,
                        fullName: fresh.fullName,
                        roles: fresh.roles,
                        createdAt: fresh.createdAt,
                    });
                }
            } catch {
                /* sessizce geç — mevcut user state ile devam */
            } finally {
                if (!cancelled) setMeLoading(false);
            }
        })();
        return () => { cancelled = true; };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    const handleSubmit = async (e) => {
        e.preventDefault();
        setFormError('');

        if (!currentPwd) {
            setFormError('Mevcut şifrenizi girin.');
            return;
        }
        const pwdErr = validatePassword(newPwd);
        if (pwdErr) {
            setFormError(pwdErr);
            return;
        }
        if (newPwd !== confirmPwd) {
            setFormError('Yeni şifreler eşleşmiyor.');
            return;
        }
        if (currentPwd === newPwd) {
            setFormError('Yeni şifre mevcut şifre ile aynı olamaz.');
            return;
        }

        setSubmitting(true);
        try {
            await changePassword(currentPwd, newPwd);
            toast.success('Şifreniz değiştirildi.');
            setCurrentPwd('');
            setNewPwd('');
            setConfirmPwd('');
        } catch (err) {
            const msg = getApiErrorMessage(err, 'Şifre değiştirilemedi.');
            setFormError(msg);
            showApiError(toast, err, msg);
        } finally {
            setSubmitting(false);
        }
    };

    return (
        <div className="violet-drift" style={{ minHeight: '100vh' }}>
            <div className="glass" style={{ display: 'flex', alignItems: 'center', padding: '20px 36px', borderBottom: '1px solid var(--glass-border)', position: 'sticky', top: 0, zIndex: 10, gap: '12px' }}>
                <div className="gradient-beam" style={{ position: 'absolute', left: 0, right: 0, bottom: 0 }} />
                <button onClick={() => navigate('/chat')} className="btn btn-ghost btn-sm">
                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                        <line x1="19" y1="12" x2="5" y2="12" /><polyline points="12 19 5 12 12 5" />
                    </svg>
                    Sohbete dön
                </button>
                <div style={{ width: '1px', height: '20px', background: 'var(--border)' }} />
                <h1 style={{ fontSize: '20px', fontWeight: 700, margin: 0, letterSpacing: '-0.02em', color: 'var(--text-primary)' }}>Profil</h1>
            </div>

            <motion.div
                initial={{ opacity: 0, y: 12 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.35, ease: 'easeOut' }}
                style={{ maxWidth: '720px', margin: '0 auto', padding: '40px 24px', display: 'flex', flexDirection: 'column', gap: '24px' }}
            >
                {/* Hesap bilgileri */}
                <section style={{
                    borderRadius: '16px',
                    padding: '28px',
                    background: 'rgba(32, 26, 58, 0.55)',
                    border: '1px solid rgba(167,139,250,0.14)',
                    backdropFilter: 'blur(24px) saturate(160%)',
                    WebkitBackdropFilter: 'blur(24px) saturate(160%)',
                    boxShadow: '0 8px 28px -10px rgba(0,0,0,0.4), inset 0 1px 0 rgba(255,255,255,0.04)',
                }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '18px', marginBottom: '24px' }}>
                        <div style={{
                            width: '64px', height: '64px', borderRadius: '18px',
                            display: 'flex', alignItems: 'center', justifyContent: 'center',
                            background: 'linear-gradient(135deg, #8b5cf6 0%, #6366f1 100%)',
                            color: '#fff', fontSize: '22px', fontWeight: 700, letterSpacing: '0.02em',
                            boxShadow: '0 10px 30px -8px rgba(99,102,241,0.5)',
                            flexShrink: 0,
                        }}>
                            {meLoading ? '…' : initials(user?.fullName)}
                        </div>
                        <div style={{ minWidth: 0 }}>
                            <h2 style={{ fontSize: '20px', fontWeight: 700, color: 'var(--text-primary)', margin: 0, letterSpacing: '-0.01em' }}>
                                {user?.fullName || '—'}
                            </h2>
                            <p style={{ fontSize: '14px', color: 'var(--text-muted)', margin: '4px 0 0', wordBreak: 'break-all' }}>
                                {user?.email || '—'}
                            </p>
                        </div>
                    </div>

                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '14px' }}>
                        <InfoField label="Roller">
                            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px' }}>
                                {(user?.roles ?? []).length === 0 && <span style={{ color: 'var(--text-muted)', fontSize: '13px' }}>—</span>}
                                {(user?.roles ?? []).map(r => (
                                    <span key={r} style={{
                                        fontSize: '12px', fontWeight: 600, padding: '3px 10px', borderRadius: '8px',
                                        background: 'rgba(139,92,246,0.18)',
                                        color: '#c4b5fd',
                                        border: '1px solid rgba(167,139,250,0.3)',
                                    }}>{r}</span>
                                ))}
                            </div>
                        </InfoField>
                        <InfoField label="Üyelik Tarihi" value={user?.createdAt ? formatDate(user.createdAt) : '—'} />
                        <InfoField label="Kullanıcı ID" value={user?.userId || '—'} mono />
                    </div>
                </section>

                {/* Şifre değiştir */}
                <section style={{
                    borderRadius: '16px',
                    padding: '28px',
                    background: 'rgba(32, 26, 58, 0.55)',
                    border: '1px solid rgba(167,139,250,0.14)',
                    backdropFilter: 'blur(24px) saturate(160%)',
                    WebkitBackdropFilter: 'blur(24px) saturate(160%)',
                    boxShadow: '0 8px 28px -10px rgba(0,0,0,0.4), inset 0 1px 0 rgba(255,255,255,0.04)',
                }}>
                    <h3 style={{ fontSize: '17px', fontWeight: 700, color: 'var(--text-primary)', margin: '0 0 6px', letterSpacing: '-0.01em' }}>
                        Şifre Değiştir
                    </h3>
                    <p style={{ fontSize: '13px', color: 'var(--text-muted)', margin: '0 0 20px' }}>
                        En az 8 karakter, bir büyük harf, bir küçük harf ve bir rakam içermelidir.
                    </p>

                    <ErrorAlert message={formError} />

                    <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                        <FormInput
                            label="Mevcut Şifre"
                            type={showCurrent ? 'text' : 'password'}
                            value={currentPwd}
                            onChange={(e) => setCurrentPwd(e.target.value)}
                            placeholder="••••••••"
                            required
                            suffix={<PasswordToggle show={showCurrent} onToggle={() => setShowCurrent(v => !v)} />}
                        />
                        <FormInput
                            label="Yeni Şifre"
                            type={showNew ? 'text' : 'password'}
                            value={newPwd}
                            onChange={(e) => setNewPwd(e.target.value)}
                            placeholder="••••••••"
                            required
                            suffix={<PasswordToggle show={showNew} onToggle={() => setShowNew(v => !v)} />}
                        />
                        <FormInput
                            label="Yeni Şifre (Tekrar)"
                            type={showConfirm ? 'text' : 'password'}
                            value={confirmPwd}
                            onChange={(e) => setConfirmPwd(e.target.value)}
                            placeholder="••••••••"
                            required
                            suffix={<PasswordToggle show={showConfirm} onToggle={() => setShowConfirm(v => !v)} />}
                        />
                        <button
                            type="submit"
                            disabled={submitting}
                            className="btn btn-primary btn-lg"
                            style={{ marginTop: '4px', fontWeight: 600 }}
                        >
                            {submitting ? 'Kaydediliyor...' : 'Şifreyi Değiştir'}
                        </button>
                    </form>
                </section>
            </motion.div>
        </div>
    );
}

function InfoField({ label, value, mono = false, children }) {
    return (
        <div>
            <p style={{ fontSize: '11px', fontWeight: 600, letterSpacing: '0.12em', textTransform: 'uppercase', color: 'rgba(255,255,255,0.45)', margin: '0 0 6px' }}>
                {label}
            </p>
            {children ?? (
                <p
                    title={mono ? value : undefined}
                    style={{
                        fontSize: mono ? '12.5px' : '14px',
                        color: 'var(--text-primary)',
                        margin: 0,
                        fontFamily: mono ? "'JetBrains Mono', monospace" : undefined,
                        whiteSpace: mono ? 'nowrap' : 'normal',
                        overflow: mono ? 'hidden' : 'visible',
                        textOverflow: mono ? 'ellipsis' : 'clip',
                        wordBreak: mono ? 'normal' : 'break-word',
                    }}>
                    {value}
                </p>
            )}
        </div>
    );
}
