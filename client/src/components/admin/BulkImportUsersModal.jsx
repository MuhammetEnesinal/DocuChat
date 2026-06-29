import { useRef, useState } from 'react';
import Modal from '../shared/Modal';

/**
 * Excel ile toplu kullanıcı yükleme modal'ı.
 *  - "Şablon İndir" → boş .xlsx + örnek satır + şifre kuralları
 *  - File picker veya drag-drop
 *  - Yükleme sonrası per-row sonuç tablosu (başarılı/atlanan + sebep)
 *
 * Props:
 *  - open: bool
 *  - onClose: () => void
 *  - onDownloadTemplate: () => void
 *  - onImport: (file: File) => Promise
 *  - loading: bool
 *  - result: { totalRows, successCount, skippedCount, results: [...] } | null
 */
export default function BulkImportUsersModal({
    open, onClose, onDownloadTemplate, onImport, loading, result,
}) {
    const fileInputRef = useRef(null);
    const [dragOver, setDragOver] = useState(false);
    const [selectedFile, setSelectedFile] = useState(null);

    if (!open) return null;

    const handleFileSelect = (file) => {
        if (!file) return;
        const ext = file.name.split('.').pop()?.toLowerCase();
        if (ext !== 'xlsx') {
            alert('Sadece .xlsx formatı kabul edilir.');
            return;
        }
        setSelectedFile(file);
    };

    const handleUpload = async () => {
        if (!selectedFile) return;
        await onImport(selectedFile);
        // Result modal'da gösterilir; dosyayı temizle ki tekrar yüklensin diye
        setSelectedFile(null);
        if (fileInputRef.current) fileInputRef.current.value = '';
    };

    const handleDrop = (e) => {
        e.preventDefault();
        setDragOver(false);
        const file = e.dataTransfer.files?.[0];
        handleFileSelect(file);
    };

    return (
        <Modal title="Excel ile Toplu Kullanıcı Yükleme" onClose={onClose} maxWidth="max-w-2xl">
            <div className="p-6 space-y-4">
                {/* Şablon indir */}
                <div className="rounded-lg p-4"
                    style={{ background: 'rgba(167,139,250,0.08)', border: '1px solid rgba(167,139,250,0.2)' }}>
                    <div className="flex items-center justify-between">
                        <div>
                            <p className="text-sm font-medium" style={{ color: '#e9e5ff' }}>
                                Excel Şablonu
                            </p>
                            <p className="text-xs mt-1" style={{ color: 'var(--gray-light)' }}>
                                Sütunlar: <strong>Ad Soyad, E-posta, Şifre</strong> — Şifre kuralları şablonun içinde.
                            </p>
                        </div>
                        <button
                            onClick={onDownloadTemplate}
                            className="px-4 py-2 rounded-lg text-sm font-medium flex items-center gap-2"
                            style={{
                                background: 'rgba(167,139,250,0.2)',
                                border: '1px solid rgba(167,139,250,0.4)',
                                color: '#c4b5fd',
                                cursor: 'pointer',
                            }}
                            onMouseEnter={e => { e.currentTarget.style.background = 'rgba(167,139,250,0.3)'; e.currentTarget.style.color = '#fff'; }}
                            onMouseLeave={e => { e.currentTarget.style.background = 'rgba(167,139,250,0.2)'; e.currentTarget.style.color = '#c4b5fd'; }}
                        >
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                                <polyline points="7 10 12 15 17 10" />
                                <line x1="12" y1="15" x2="12" y2="3" />
                            </svg>
                            Şablonu İndir
                        </button>
                    </div>
                </div>

                {/* Drag-drop alanı */}
                <div
                    onDragOver={(e) => { e.preventDefault(); setDragOver(true); }}
                    onDragLeave={() => setDragOver(false)}
                    onDrop={handleDrop}
                    onClick={() => fileInputRef.current?.click()}
                    style={{
                        border: '2px dashed ' + (dragOver ? '#a78bfa' : 'rgba(167,139,250,0.3)'),
                        borderRadius: '12px',
                        padding: '32px',
                        textAlign: 'center',
                        cursor: 'pointer',
                        background: dragOver ? 'rgba(167,139,250,0.08)' : 'rgba(255,255,255,0.02)',
                        transition: 'all 0.15s',
                    }}
                >
                    <input
                        ref={fileInputRef}
                        type="file"
                        accept=".xlsx"
                        style={{ display: 'none' }}
                        onChange={(e) => handleFileSelect(e.target.files?.[0])}
                    />
                    <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5"
                        style={{ color: dragOver ? '#a78bfa' : '#64748b', margin: '0 auto 8px' }}>
                        <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                        <polyline points="17 8 12 3 7 8" />
                        <line x1="12" y1="3" x2="12" y2="15" />
                    </svg>
                    {selectedFile ? (
                        <>
                            <p className="text-sm font-medium" style={{ color: '#86efac' }}>
                                ✓ {selectedFile.name}
                            </p>
                            <p className="text-xs mt-1" style={{ color: 'var(--gray-light)' }}>
                                {(selectedFile.size / 1024).toFixed(1)} KB — değiştirmek için tıkla
                            </p>
                        </>
                    ) : (
                        <>
                            <p className="text-sm font-medium" style={{ color: '#e2e8f0' }}>
                                .xlsx dosyasını sürükle bırak veya tıkla
                            </p>
                            <p className="text-xs mt-1" style={{ color: 'var(--gray-light)' }}>
                                Sadece Excel formatı (.xlsx)
                            </p>
                        </>
                    )}
                </div>

                {/* Yükle butonu */}
                {selectedFile && !result && (
                    <button
                        onClick={handleUpload}
                        disabled={loading}
                        className="w-full px-4 py-3 rounded-lg text-sm font-semibold flex items-center justify-center gap-2"
                        style={{
                            background: loading ? 'rgba(139,92,246,0.5)' : 'linear-gradient(135deg, #8b5cf6, #a78bfa)',
                            border: 'none',
                            color: 'white',
                            cursor: loading ? 'not-allowed' : 'pointer',
                        }}
                    >
                        {loading && (
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" className="animate-spin">
                                <path d="M21 12a9 9 0 1 1-6.219-8.56" />
                            </svg>
                        )}
                        {loading ? 'Yükleniyor…' : 'Yükle ve İçeri Aktar'}
                    </button>
                )}

                {/* Sonuç tablosu */}
                {result && (
                    <div className="space-y-3">
                        <div className="flex gap-3">
                            <div className="flex-1 rounded-lg p-3 text-center"
                                style={{ background: 'rgba(34,197,94,0.1)', border: '1px solid rgba(34,197,94,0.3)' }}>
                                <p className="text-2xl font-bold" style={{ color: '#86efac' }}>{result.successCount}</p>
                                <p className="text-xs" style={{ color: 'var(--gray-light)' }}>Başarılı</p>
                            </div>
                            <div className="flex-1 rounded-lg p-3 text-center"
                                style={{ background: 'rgba(251,146,60,0.1)', border: '1px solid rgba(251,146,60,0.3)' }}>
                                <p className="text-2xl font-bold" style={{ color: '#fb923c' }}>{result.skippedCount}</p>
                                <p className="text-xs" style={{ color: 'var(--gray-light)' }}>Atlandı</p>
                            </div>
                            <div className="flex-1 rounded-lg p-3 text-center"
                                style={{ background: 'rgba(148,163,184,0.1)', border: '1px solid rgba(148,163,184,0.3)' }}>
                                <p className="text-2xl font-bold" style={{ color: '#cbd5e1' }}>{result.totalRows}</p>
                                <p className="text-xs" style={{ color: 'var(--gray-light)' }}>Toplam</p>
                            </div>
                        </div>

                        {/* Per-row detay */}
                        <div className="rounded-lg overflow-hidden"
                            style={{ border: '1px solid rgba(255,255,255,0.08)', maxHeight: '300px', overflowY: 'auto' }}>
                            <table className="w-full text-xs">
                                <thead style={{ background: 'rgba(255,255,255,0.04)', position: 'sticky', top: 0 }}>
                                    <tr>
                                        <th className="px-3 py-2 text-left" style={{ color: 'var(--gray-light)' }}>Satır</th>
                                        <th className="px-3 py-2 text-left" style={{ color: 'var(--gray-light)' }}>E-posta</th>
                                        <th className="px-3 py-2 text-left" style={{ color: 'var(--gray-light)' }}>Durum</th>
                                        <th className="px-3 py-2 text-left" style={{ color: 'var(--gray-light)' }}>Sebep</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {result.results.map((r, i) => (
                                        <tr key={i} style={{ borderTop: '1px solid rgba(255,255,255,0.05)' }}>
                                            <td className="px-3 py-2" style={{ color: '#94a3b8' }}>{r.row}</td>
                                            <td className="px-3 py-2" style={{ color: '#e2e8f0' }}>{r.email || '—'}</td>
                                            <td className="px-3 py-2">
                                                {r.status === 'success' ? (
                                                    <span style={{ color: '#86efac', fontWeight: 600 }}>✓ Başarılı</span>
                                                ) : (
                                                    <span style={{ color: '#fb923c', fontWeight: 600 }}>⚠ Atlandı</span>
                                                )}
                                            </td>
                                            <td className="px-3 py-2" style={{ color: 'var(--gray-light)' }}>
                                                {r.reason || '—'}
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>

                        <button
                            onClick={onClose}
                            className="w-full px-4 py-2 rounded-lg text-sm font-medium"
                            style={{
                                background: 'rgba(255,255,255,0.08)',
                                border: '1px solid rgba(255,255,255,0.15)',
                                color: '#e2e8f0',
                                cursor: 'pointer',
                            }}
                        >
                            Kapat
                        </button>
                    </div>
                )}
            </div>
        </Modal>
    );
}
