import Modal from '../shared/Modal';

export default function UserModal({ onClose, onSubmit, user, onChange, error, loading, isEdit }) {
    const fields = [
        { label: 'Ad Soyad', key: 'fullName', type: 'text', placeholder: 'Ad Soyad', required: true },
        { label: 'E-posta', key: 'email', type: 'email', placeholder: 'ornek@sirket.com', required: true },
        {
            label: isEdit ? 'Yeni Şifre (değiştirmek için doldurun)' : 'Şifre',
            key: 'password', type: 'password',
            placeholder: isEdit ? 'Boş bırakılırsa değişmez' : 'En az 8 karakter',
            required: !isEdit,
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
                            onChange={(e) => onChange(key, e.target.value)}
                            style={{ width: '100%', padding: '12px 16px', borderRadius: '10px', fontSize: '14px', color: 'var(--text-primary)', background: 'var(--surface2)', border: '1px solid var(--border)', outline: 'none', boxSizing: 'border-box' }}
                            onFocus={(e) => e.target.style.borderColor = 'var(--accent)'}
                            onBlur={(e) => e.target.style.borderColor = 'var(--border)'}
                        />
                    </div>
                ))}
                <p style={{ fontSize: '12px', color: '#475569', margin: 0 }}>Şifre: büyük/küçük harf, rakam ve özel karakter içermeli</p>
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
