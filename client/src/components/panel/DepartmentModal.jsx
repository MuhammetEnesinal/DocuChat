import { useState } from 'react';
import Modal from '../shared/Modal';

// Departman ekleme/düzenleme penceresi (UserModal deseni). Ad + Kod.
export default function DepartmentModal({ onClose, onSubmit, isEdit, initialName = '', initialCode = '' }) {
    const [name, setName] = useState(initialName);
    const [code, setCode] = useState(initialCode);
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(false);

    // Backend validator ile aynı kural: boşluk/özel karakter yok.
    const codeInvalid = code.length > 0 && !/^[A-Za-z0-9ÇĞİıÖŞÜçğöşü_-]+$/.test(code);

    const handleSubmit = async (e) => {
        e.preventDefault();
        setError('');
        if (codeInvalid) { setError('Departman kodu boşluk veya özel karakter içeremez.'); return; }
        setLoading(true);
        try {
            await onSubmit(name.trim(), code.trim());
            onClose();
        } catch (err) {
            setError(err?.response?.data?.error?.message || 'İşlem başarısız.');
        } finally {
            setLoading(false);
        }
    };

    const inputStyle = (invalid) => ({
        width: '100%', padding: '12px 16px', borderRadius: '10px', fontSize: '14px',
        color: 'var(--text-primary)', background: 'var(--surface2)',
        border: `1px solid ${invalid ? 'rgba(239,68,68,0.5)' : 'var(--border)'}`,
        outline: 'none', boxSizing: 'border-box',
    });

    return (
        <Modal title={isEdit ? 'Departmanı Düzenle' : 'Yeni Departman'} onClose={onClose}>
            <form onSubmit={handleSubmit} style={{ padding: '24px', display: 'flex', flexDirection: 'column', gap: '16px' }}>
                {error && (
                    <div style={{ padding: '12px', borderRadius: '8px', fontSize: '14px', background: 'rgba(239,68,68,0.1)', border: '1px solid rgba(239,68,68,0.3)', color: '#fca5a5' }}>{error}</div>
                )}

                <div>
                    <label style={{ display: 'block', fontSize: '14px', fontWeight: 500, marginBottom: '8px', color: 'var(--text-muted)' }}>Departman Adı</label>
                    <input
                        value={name} onChange={(e) => setName(e.target.value)}
                        required maxLength={150} placeholder="örn. Yazılım" autoFocus
                        style={inputStyle(false)}
                    />
                </div>

                <div>
                    <label style={{ display: 'block', fontSize: '14px', fontWeight: 500, marginBottom: '8px', color: 'var(--text-muted)' }}>
                        Departman Kodu
                    </label>
                    <input
                        value={code} onChange={(e) => setCode(e.target.value)}
                        required maxLength={20} placeholder="örn. YAZILIM"
                        style={inputStyle(codeInvalid)}
                    />
                    <p style={{ fontSize: '12px', color: codeInvalid ? '#f87171' : 'var(--gray-light)', marginTop: '6px' }}>
                        {codeInvalid
                            ? 'Boşluk veya özel karakter kullanılamaz (harf, rakam, - ve _ serbest).'
                            : 'Excel ile toplu yüklemede departman bu KOD ile yazılır. Büyük/küçük harf duyarlıdır.'}
                    </p>
                </div>

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
