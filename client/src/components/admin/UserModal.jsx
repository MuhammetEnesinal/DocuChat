import { useState } from 'react';
import Modal from '../shared/Modal';

const TR_UPPER = 'A-ZÇĞİÖŞÜ';
const TR_LOWER = 'a-zçğıöşü';

// Personel kodu kuralı. Backend validator ile aynı: harf+rakam karışık, boşluksuz, en az 6 karakter.
function validatePersonnelCode(code) {
    if (!code) return null;
    const rules = [];
    if (code.length < 6) rules.push('En az 6 karakter');
    if (/\s/.test(code)) rules.push('Boşluk içeremez');
    if (!new RegExp(`[${TR_UPPER}${TR_LOWER}]`).test(code)) rules.push('Harf');
    if (!/\d/.test(code)) rules.push('Rakam');
    return rules.length === 0 ? null : rules.join(', ');
}

const MAX_LENGTHS = { fullName: 100, email: 256, personnelCode: 50 };

export default function UserModal({ onClose, onSubmit, user, onChange, error, loading, isEdit }) {
    const [touched, setTouched] = useState({});

    const personnelCodeError = validatePersonnelCode(user.personnelCode);
    const showPersonnelCodeError = touched.personnelCode && personnelCodeError;

    const handleBlur = (key) => setTouched(prev => ({ ...prev, [key]: true }));

    const fields = [
        { label: 'Ad Soyad', key: 'fullName', type: 'text', placeholder: 'Ad Soyad', required: true },
        { label: 'E-posta', key: 'email', type: 'email', placeholder: 'ornek@sirket.com', required: true },
        {
            // Personel kodu kimlik bilgisidir; oluşturmada ayrıca ilk şifre olarak kullanılır.
            label: isEdit ? 'Personel Kodu' : 'Personel Kodu (ilk şifre olarak kullanılır)',
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
                            style={{ width: '100%', padding: '12px 16px', borderRadius: '10px', fontSize: '14px', color: 'var(--text-primary)', background: 'var(--surface2)', border: `1px solid ${(key === 'personnelCode' && showPersonnelCodeError) ? 'rgba(239,68,68,0.5)' : 'var(--border)'}`, outline: 'none', boxSizing: 'border-box' }}
                            onFocus={(e) => e.target.style.borderColor = 'var(--accent)'}
                        />
                        {key === 'personnelCode' && showPersonnelCodeError && (
                            <p style={{ fontSize: '12px', color: '#f87171', marginTop: '6px' }}>
                                Personel kodu şunları içermeli: {personnelCodeError}
                            </p>
                        )}
                    </div>
                ))}
                {/* flexWrap + basis 140px: dar ekranda butonlar alt alta tam genişlik geçer */}
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: '10px 12px', marginTop: '8px' }}>
                    <button type="button" onClick={onClose}
                        style={{ flex: '1 1 140px', padding: '12px', borderRadius: '10px', fontSize: '14px', fontWeight: 500, color: 'var(--text-secondary)', background: 'var(--surface2)', border: '1px solid var(--border)', cursor: 'pointer' }}>
                        İptal
                    </button>
                    <button type="submit" disabled={loading}
                        style={{ flex: '1 1 140px', padding: '12px', borderRadius: '10px', fontSize: '14px', fontWeight: 600, color: 'var(--text-primary)', background: loading ? 'var(--navy-light)' : 'var(--accent)', border: 'none', cursor: loading ? 'not-allowed' : 'pointer' }}>
                        {loading ? (isEdit ? 'Kaydediliyor...' : 'Oluşturuluyor...') : (isEdit ? 'Kaydet' : 'Oluştur')}
                    </button>
                </div>
            </form>
        </Modal>
    );
}
