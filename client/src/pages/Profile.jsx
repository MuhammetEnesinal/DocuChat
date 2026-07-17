import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { motion } from 'framer-motion';
import { getMe, changePassword } from '../services/api';
import { useAuth } from '../hooks/useAuth';
import { useToast } from '../components/shared/Toast';
import { formatDate, showApiError, getApiErrorMessage, roleLabel, departmentLabel } from '../lib/format';
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

// Backend kuralıyla birebir (ChangePasswordRequestDtoValidator):
// 8+ karakter, büyük harf, küçük harf, rakam, özel karakter.
function validatePassword(pwd) {
    if (!pwd) return 'Yeni şifre boş olamaz.';
    if (pwd.length < 8) return 'Şifre en az 8 karakter olmalıdır.';
    if (!/[A-ZÇĞİÖŞÜ]/.test(pwd)) return 'Şifre en az bir büyük harf içermelidir.';
    if (!/[a-zçğıöşü]/.test(pwd)) return 'Şifre en az bir küçük harf içermelidir.';
    if (!/\d/.test(pwd)) return 'Şifre en az bir rakam içermelidir.';
    if (!/[^a-zA-ZÇĞİÖŞÜçğıöşü0-9]/.test(pwd)) return 'Şifre en az bir özel karakter içermelidir.';
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
    const [formError, setFormError] = useState('');
    const [submitting, setSubmitting] = useState(false);

    useEffect(() => {
        let cancelled = false;
        (async () => {
            try {
                const res = await getMe();
                const fresh = res.data?.data;
                if (!cancelled && fresh) {
                    // DİKKAT: user nesnesi burada tamamen yeniden yazılır. Eklenmeyen her alan
                    // localStorage'dan DÜŞER — departments düşerse yönetici belge yükleyemez hale
                    // gelir. Yeni alan eklerken buraya da eklemeyi unutma.
                    setAuth(token, {
                        userId: fresh.id,
                        email: fresh.email,
                        fullName: fresh.fullName,
                        personnelCode: fresh.personnelCode,
                        roles: fresh.roles,
                        departments: fresh.departments ?? [],
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

    // Zengin cam kart; her iki bölüm de kullanır. Padding clamp ile dar ekranda daralır,
    // minWidth 0 içerik taşmasını engeller.
    const cardStyle = {
        borderRadius: '18px',
        padding: 'clamp(20px, 5vw, 32px) clamp(14px, 4.5vw, 28px)',
        minWidth: 0,
        background: 'linear-gradient(180deg, rgba(42, 33, 72, 0.55) 0%, rgba(28, 22, 50, 0.55) 100%)',
        border: '1px solid rgba(var(--accent-light-rgb),0.18)',
        backdropFilter: 'blur(28px) saturate(170%)',
        WebkitBackdropFilter: 'blur(28px) saturate(170%)',
        boxShadow: '0 18px 50px -18px rgba(0,0,0,0.6), 0 0 50px -22px rgba(var(--accent-rgb),0.4), inset 0 1px 0 rgba(255,255,255,0.07)',
    };

    return (
        <div style={{ minHeight: '100vh', position: 'relative', background: '#000', overflowX: 'clip' }}>
            {/* Siyah taban + merkezde mor ışıma kubbesi zemin */}
            <div aria-hidden style={{
                position: 'fixed', inset: 0, zIndex: 0, pointerEvents: 'none',
                background:
                    'radial-gradient(ellipse 46% 42% at 50% 46%, rgba(var(--accent-rgb),0.38), rgba(var(--accent-deep-rgb),0.18) 45%, transparent 72%),' +
                    'radial-gradient(ellipse 70% 60% at 50% 55%, rgba(var(--accent-deep-rgb),0.12), transparent 75%)',
            }} />
            <div className="glass" style={{ background: 'rgba(28, 32, 52, 0.98)', display: 'flex', alignItems: 'center', height: '74px', padding: '0 clamp(12px, 3.5vw, 28px)', borderBottom: '1px solid var(--glass-border)', position: 'sticky', top: 0, zIndex: 10, gap: '10px', minWidth: 0 }}>
                <div className="gradient-beam" style={{ position: 'absolute', left: 0, right: 0, bottom: 0 }} />
                <button onClick={() => navigate('/chat')} className="btn btn-ghost btn-sm">
                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                        <line x1="19" y1="12" x2="5" y2="12" /><polyline points="12 19 5 12 12 5" />
                    </svg>
                    <span className="profile-back-text">Sohbete dön</span>
                </button>
                <div style={{ flex: 1 }} />
                {/* Profil kimliği — en sağda; dar ekranda başlık ellipsis ile küçülür, taşmaz */}
                <div style={{ display: 'flex', alignItems: 'center', gap: '10px', minWidth: 0, flexShrink: 1 }}>
                    <div style={{ width: '30px', height: '30px', borderRadius: '9px', display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'var(--gradient-accent)', boxShadow: '0 6px 16px -6px rgba(var(--accent-rgb),0.6)', flexShrink: 0 }}>
                        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" /><circle cx="12" cy="7" r="4" /></svg>
                    </div>
                    <h1 style={{ fontSize: '18px', fontWeight: 700, margin: 0, letterSpacing: '-0.02em', color: 'var(--text-primary)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>Profil</h1>
                </div>
            </div>

            <motion.div
                initial={{ opacity: 0, y: 12 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.35, ease: 'easeOut' }}
                style={{ position: 'relative', zIndex: 1, maxWidth: '720px', margin: '0 auto', padding: 'clamp(20px, 5vw, 40px) clamp(12px, 4vw, 24px)', display: 'flex', flexDirection: 'column', gap: '24px' }}
            >
                {/* Hesap bilgileri */}
                <section style={cardStyle}>
                    {/* Hero: ortalı avatar + isim + e-posta */}
                    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', textAlign: 'center', marginBottom: '26px', minWidth: 0 }}>
                        <div className="profile-hero-avatar" style={{
                            width: '84px', height: '84px', borderRadius: '24px',
                            display: 'flex', alignItems: 'center', justifyContent: 'center',
                            background: 'linear-gradient(135deg, var(--accent-light) 0%, var(--accent) 45%, var(--accent-deep) 100%)',
                            color: '#fff', fontSize: '30px', fontWeight: 700, letterSpacing: '0.02em',
                            boxShadow: '0 16px 44px -10px rgba(var(--accent-rgb),0.75), inset 0 1px 0 rgba(255,255,255,0.35)',
                            marginBottom: '16px',
                        }}>
                            {meLoading ? '…' : initials(user?.fullName)}
                        </div>
                        <h2 title={user?.fullName} style={{ fontSize: '22px', fontWeight: 700, color: 'var(--text-primary)', margin: 0, letterSpacing: '-0.01em', maxWidth: '100%', overflowWrap: 'anywhere', display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden' }}>
                            {user?.fullName || '—'}
                        </h2>
                        <p title={user?.email} style={{ fontSize: '14px', color: 'var(--text-muted)', margin: '5px 0 0', maxWidth: '100%', overflowWrap: 'anywhere', display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden' }}>
                            {user?.email || '—'}
                        </p>
                    </div>

                    {/* İnce ayraç */}
                    <div style={{ height: '1px', background: 'linear-gradient(90deg, transparent, rgba(var(--accent-light-rgb),0.22), transparent)', margin: '0 0 22px' }} />

                    {/* Bilgi mini-kartları (ikonlu). Grid index.css'te (.profile-info-grid):
                        4 sütun → 700px altı 2 → 480px altı alt alta tam genişlik.
                        Media query gerektiği için inline stil değil, sınıf kullanıldı. */}
                    <div className="profile-info-grid">
                        <InfoField label="Yetki" icon={
                            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" /></svg>
                        }>
                            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px' }}>
                                {(user?.roles ?? []).length === 0 && <span style={{ color: 'var(--text-muted)', fontSize: '13px' }}>—</span>}
                                {(user?.roles ?? []).map(r => (
                                    <span key={r} style={{
                                        fontSize: '12px', fontWeight: 700, padding: '4px 12px', borderRadius: '8px',
                                        background: 'var(--gradient-accent)',
                                        color: '#fff',
                                        boxShadow: '0 4px 12px -4px rgba(var(--accent-rgb),0.6), inset 0 1px 0 rgba(255,255,255,0.2)',
                                    }}>{roleLabel(r)}</span>
                                ))}
                            </div>
                        </InfoField>
                        {/* Departmanlar — izolasyonun temeli, kullanıcı hangi kapsamda olduğunu görsün.
                            children yerine value: Üyelik Tarihi ile birebir aynı tipografi.
                            Çoklu departman virgülle ayrılır, sığmazsa alt satıra sarar. */}
                        <InfoField
                            label="Departman"
                            value={(user?.departments ?? []).map(departmentLabel).join(', ') || '—'}
                            icon={
                                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M3 21h18" /><path d="M5 21V7l8-4v18" /><path d="M19 21V11l-6-4" /><line x1="9" y1="9" x2="9" y2="9.01" /><line x1="9" y1="12" x2="9" y2="12.01" /><line x1="9" y1="15" x2="9" y2="15.01" /></svg>
                            }
                        />
                        <InfoField label="Üyelik Tarihi" value={user?.createdAt ? formatDate(user.createdAt) : '—'} icon={
                            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><rect x="3" y="4" width="18" height="18" rx="2" /><line x1="16" y1="2" x2="16" y2="6" /><line x1="8" y1="2" x2="8" y2="6" /><line x1="3" y1="10" x2="21" y2="10" /></svg>
                        } />
                        <InfoField label="Personel Kodu" value={user?.personnelCode || '—'} mono icon={
                            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><line x1="4" y1="9" x2="20" y2="9" /><line x1="4" y1="15" x2="20" y2="15" /><line x1="10" y1="3" x2="8" y2="21" /><line x1="16" y1="3" x2="14" y2="21" /></svg>
                        } />
                    </div>
                </section>

                {/* Şifre değiştir */}
                <section style={cardStyle}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '6px' }}>
                        <div className="profile-section-icon" style={{ width: '36px', height: '36px', borderRadius: '11px', flexShrink: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'rgba(var(--accent-rgb),0.24)', color: '#d8ccff', border: '1px solid rgba(var(--accent-light-rgb),0.35)' }}>
                            <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><rect x="3" y="11" width="18" height="11" rx="2" /><path d="M7 11V7a5 5 0 0 1 10 0v4" /></svg>
                        </div>
                        <h3 style={{ fontSize: '17px', fontWeight: 700, color: 'var(--text-primary)', margin: 0, letterSpacing: '-0.01em', minWidth: 0 }}>
                            Şifre Değiştir
                        </h3>
                    </div>
                    <p style={{ fontSize: '13px', color: 'var(--text-secondary)', margin: '0 0 20px' }}>
                        En az 8 karakter; bir büyük harf, bir küçük harf, bir rakam ve bir özel karakter içermelidir.
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
                            type="password"
                            value={confirmPwd}
                            onChange={(e) => setConfirmPwd(e.target.value)}
                            placeholder="••••••••"
                            required
                        />
                        <button
                            type="submit"
                            disabled={submitting}
                            className="btn btn-primary btn-lg"
                            style={{ marginTop: '4px', fontWeight: 600, width: '100%', minWidth: 0, whiteSpace: 'normal' }}
                        >
                            {submitting ? 'Kaydediliyor...' : 'Şifreyi Değiştir'}
                        </button>
                    </form>
                </section>
            </motion.div>
        </div>
    );
}

function InfoField({ label, value, mono = false, icon, children }) {
    return (
        <div style={{
            borderRadius: '14px', padding: '14px 15px',
            background: 'rgba(255,255,255,0.035)',
            border: '1px solid rgba(var(--accent-light-rgb),0.12)',
            // flexWrap + basis 110px: yazıya 110px'ten az yer kalırsa ikonun ALTINA tam genişlik
            // sarar — yazı asla 20-30px'lik şeride sıkışıp harf harf kırılmaz.
            display: 'flex', alignItems: 'flex-start', flexWrap: 'wrap', gap: '10px 12px',
        }}>
            {icon && (
                <div className="profile-section-icon" style={{ width: '34px', height: '34px', borderRadius: '10px', flexShrink: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'rgba(var(--accent-rgb),0.24)', color: '#d8ccff', border: '1px solid rgba(var(--accent-light-rgb),0.3)' }}>
                    {icon}
                </div>
            )}
            {/* basis 92px: 4 sütun düzeninde kart ~148px → 34(ikon)+12(gap)+92 = 138, yazı ikonun
                yanında kalır. Daha dar kalırsa yine alta tam genişlik sarar (harf harf kırılmaz). */}
            <div style={{ minWidth: 0, flex: '1 1 92px' }}>
                <p style={{ fontSize: '11px', fontWeight: 700, letterSpacing: '0.1em', textTransform: 'uppercase', color: 'var(--text-secondary)', margin: '0 0 4px' }}>
                    {label}
                </p>
                {children ?? (
                    <p
                        title={mono ? value : undefined}
                        style={{
                            fontSize: mono ? '12.5px' : '14px',
                            fontWeight: 500,
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
        </div>
    );
}
