import Modal from '../shared/Modal';

export default function UserModal({ onClose, onSubmit, user, onChange, error, loading }) {
    return (
        <Modal title="Yeni Kullanıcı" onClose={onClose}>
            <form onSubmit={onSubmit} style={{ padding: '24px', display: 'flex', flexDirection: 'column', gap: '16px' }}>
                {error && (
                    <div style={{ padding: '12px', borderRadius: '8px', fontSize: '14px', background: 'rgba(239,68,68,0.1)', border: '1px solid rgba(239,68,68,0.3)', color: '#fca5a5' }}>{error}</div>
                )}
                {[
                    { label: 'Ad Soyad', key: 'fullName', type: 'text', placeholder: 'Ad Soyad' },
                    { label: 'E-posta', key: 'email', type: 'email', placeholder: 'ornek@sirket.com' },
                    { label: 'Şifre', key: 'password', type: 'password', placeholder: 'En az 8 karakter' },
                ].map(({ label, key, type, placeholder }) => (
                    <div key={key}>
                        <label style={{ display: 'block', fontSize: '14px', fontWeight: 500, marginBottom: '8px', color: '#94a3b8' }}>{label}</label>
                        <input type={type} value={user[key]} required placeholder={placeholder}
                            onChange={(e) => onChange(key, e.target.value)}
                            style={{ width: '100%', padding: '12px 16px', borderRadius: '10px', fontSize: '14px', color: 'white', background: 'var(--surface2)', border: '1px solid var(--border)', outline: 'none', boxSizing: 'border-box' }}
                            onFocus={(e) => e.target.style.borderColor = 'var(--accent)'}
                            onBlur={(e) => e.target.style.borderColor = 'var(--border)'}
                        />
                    </div>
                ))}
                <p style={{ fontSize: '12px', color: '#475569', margin: 0 }}>Şifre: büyük/küçük harf, rakam ve özel karakter içermeli</p>
                <div style={{ display: 'flex', gap: '12px', marginTop: '8px' }}>
                    <button type="button" onClick={onClose}
                        style={{ flex: 1, padding: '12px', borderRadius: '10px', fontSize: '14px', fontWeight: 500, color: '#94a3b8', background: 'var(--surface2)', border: '1px solid var(--border)', cursor: 'pointer' }}>
                        İptal
                    </button>
                    <button type="submit" disabled={loading}
                        style={{ flex: 1, padding: '12px', borderRadius: '10px', fontSize: '14px', fontWeight: 600, color: 'white', background: loading ? 'var(--navy-light)' : 'var(--accent)', border: 'none', cursor: loading ? 'not-allowed' : 'pointer' }}>
                        {loading ? 'Oluşturuluyor...' : 'Oluştur'}
                    </button>
                </div>
            </form>
        </Modal>
    );
}