import { useState } from 'react';
import Modal from '../shared/Modal';

const TR_UPPER = 'A-ZÇĞİÖŞÜ';
const TR_LOWER = 'a-zçğıöşü';

function validatePassword(pwd) {
    if (!pwd) return null;
    const rules = [];
    if (pwd.length < 8) rules.push('En az 8 karakter');
    if (!new RegExp(`[${TR_UPPER}]`).test(pwd)) rules.push('Büyük harf');
    if (!new RegExp(`[${TR_LOWER}]`).test(pwd)) rules.push('Küçük harf');
    if (!/\d/.test(pwd)) rules.push('Rakam');
    if (!new RegExp(`[^${TR_UPPER}${TR_LOWER}0-9]`).test(pwd)) rules.push('Özel karakter');
    return rules.length === 0 ? null : rules.join(', ');
}

// Personel kodu = yeni kullanıcının ilk şifresi. Backend validator ile aynı kural: harf+rakam,
// boşluksuz, en az 6 karakter. (Büyük/küçük harf + sembol zorunlu değil.)
function validatePersonnelCode(code) {
    if (!code) return null;
    const rules = [];
    if (code.length < 6) rules.push('En az 6 karakter');
    if (/\s/.test(code)) rules.push('Boşluk içeremez');
    if (!new RegExp(`[${TR_UPPER}${TR_LOWER}]`).test(code)) rules.push('Harf');
    if (!/\d/.test(code)) rules.push('Rakam');
    return rules.length === 0 ? null : rules.join(', ');
}

const MAX_LENGTHS = { fullName: 100, email: 256, password: 128, personnelCode: 50 };

export default function UserModal({ onClose, onSubmit, user, onChange, error, loading, isEdit }) {
    const [touched, setTouched] = useState({});

    const passwordError = validatePassword(user.password);
    const showPasswordError = touched.password && passwordError;
    const personnelCodeError = validatePersonnelCode(user.personnelCode);
    const showPersonnelCodeError = touched.personnelCode && personnelCodeError;

    const handleBlur = (key) => setTouched(prev => ({ ...prev, [key]: true }));

    const fields = [
        { label: 'Ad Soyad', key: 'fullName', type: 'text', placeholder: 'Ad Soyad', required: true },
        { label: 'E-posta', key: 'email', type: 'email', placeholder: 'ornek@sirket.com', required: true },
        isEdit
            ? {
                label: 'Yeni Şifre (değiştirmek için doldurun)',
                key: 'password', type: 'password',
                placeholder: 'Boş bırakılırsa değişmez',
                required: false,
            }
            : {
                // Yeni kullanıcıda şifre girilmez — personel kodu ilk şifre olarak kullanılır.
                label: 'Personel Kodu (ilk şifre olarak kullanılır)',
                key: 'personnelCode', type: 'text',
                placeholder: 'örn. EMP1001',
                required: true,
            },
    ];

    return (
        <Modal title={isEdit ? 'Kullanıcıyı Düzenle' : 'Yeni Kullanıcı'} onClose={onClose}>
            <form onSubmit={onSubmit} style={{ padding: '24px', display: 'flex', flexDirection: 'column', gap: '16px' }}>
                {error && (
                    <div style={{ padding: '12px', borderRadius: '8px', fontSize: '14px', background: 'rgba(239,68,68,0.1)', border: '1px solid rgba(239,68,68,0.3)', color: '#fca5a5' }}>{error}</div>
                )}
                {fields.map(({ label, key, type, placeholder, required }) => (
                    <div key={key}>
                        <label style={{ display: 'block', fontSize: '14px', fontWeight: 500, marginBottom: '8px', color: 'var(--text-muted)' }}>{label}</label>
                        <input
                            type={type}
                            value={user[key]}
                            required={required}
                            placeholder={placeholder}
                            maxLength={MAX_LENGTHS[key]}
                            onChange={(e) => onChange(key, e.target.value)}
                            onBlur={() => handleBlur(key)}
                            style={{ width: '100%', padding: '12px 16px', borderRadius: '10px', fontSize: '14px', color: 'var(--text-primary)', background: 'var(--surface2)', border: `1px solid ${(key === 'password' && showPasswordError) || (key === 'personnelCode' && showPersonnelCodeError) ? 'rgba(239,68,68,0.5)' : 'var(--border)'}`, outline: 'none', boxSizing: 'border-box' }}
                            onFocus={(e) => e.target.style.borderColor = 'var(--accent)'}
                        />
                        {key === 'password' && showPasswordError && (
                            <p style={{ fontSize: '12px', color: '#f87171', marginTop: '6px' }}>
                                Şifre şunları içermeli: {passwordError}
                            </p>
                        )}
                        {key === 'personnelCode' && showPersonnelCodeError && (
                            <p style={{ fontSize: '12px', color: '#f87171', marginTop: '6px' }}>
                                Personel kodu şunları içermeli: {personnelCodeError}
                            </p>
                        )}
                    </div>
                ))}
                <div style={{ display: 'flex', gap: '12px', marginTop: '8px' }}>
                    <button type="button" onClick={onClose}
                        style={{ flex: 1, padding: '12px', borderRadius: '10px', fontSize: '14px', fontWeight: 500, color: 'var(--text-muted)', background: 'var(--surface2)', border: '1px solid var(--border)', cursor: 'pointer' }}>
                        İptal
                    </button>
                    <button type="submit" disabled={loading}
                        style={{ flex: 1, padding: '12px', borderRadius: '10px', fontSize: '14px', fontWeight: 600, color: 'var(--text-primary)', background: loading ? 'var(--navy-light)' : 'var(--accent)', border: 'none', cursor: loading ? 'not-allowed' : 'pointer' }}>
                        {loading ? (isEdit ? 'Kaydediliyor...' : 'Oluşturuluyor...') : (isEdit ? 'Kaydet' : 'Oluştur')}
                    </button>
                </div>
            </form>
        </Modal>
    );
}
