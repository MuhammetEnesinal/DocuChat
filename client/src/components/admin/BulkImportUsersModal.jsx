import { useRef, useState } from 'react';
import Modal from '../shared/Modal';
import { useToast } from '../shared/Toast';

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
 *  - progress: { processed, total, successCount, skippedCount, lastRow, lastEmail, lastStatus } | null
 */
export default function BulkImportUsersModal({
    open, onClose, onDownloadTemplate, onImport, loading, result, progress,
}) {
    const fileInputRef = useRef(null);
    const [dragOver, setDragOver] = useState(false);
    const [selectedFile, setSelectedFile] = useState(null);
    const toast = useToast();

    if (!open) return null;

    const handleFileSelect = (file) => {
        if (!file) return;
        const ext = file.name.split('.').pop()?.toLowerCase();
        if (ext !== 'xlsx') {
            toast.error('Sadece .xlsx formatı kabul edilir.');
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
                    style={{ background: 'rgba(var(--accent-light-rgb),0.08)', border: '1px solid rgba(var(--accent-light-rgb),0.2)' }}>
                    <div className="flex items-center justify-between flex-wrap gap-3">
                        <div style={{ minWidth: 0, flex: '1 1 200px' }}>
                            <p className="text-sm font-medium" style={{ color: '#e9e5ff' }}>
                                Excel Şablonu
                            </p>
                            <p className="text-xs mt-1" style={{ color: 'var(--gray-light)' }}>
                                Sütunlar: <strong>Ad Soyad, E-posta, Personel Kodu</strong> — Personel kodu ilk şifre olarak kullanılır.
                            </p>
                        </div>
                        <button
                            onClick={onDownloadTemplate}
                            className="bulk-template-btn px-4 py-2 rounded-lg text-sm font-medium flex items-center gap-2"
                            style={{
                                background: 'rgba(var(--accent-light-rgb),0.2)',
                                border: '1px solid rgba(var(--accent-light-rgb),0.4)',
                                color: '#c4b5fd',
                                cursor: 'pointer',
                                flexShrink: 0,
                                whiteSpace: 'nowrap',
                            }}
                            onMouseEnter={e => { e.currentTarget.style.background = 'rgba(var(--accent-light-rgb),0.3)'; e.currentTarget.style.color = '#fff'; }}
                            onMouseLeave={e => { e.currentTarget.style.background = 'rgba(var(--accent-light-rgb),0.2)'; e.currentTarget.style.color = '#c4b5fd'; }}
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
                        border: '2px dashed ' + (dragOver ? 'var(--accent-light)' : 'rgba(var(--accent-light-rgb),0.3)'),
                        borderRadius: '12px',
                        padding: '32px',
                        textAlign: 'center',
                        cursor: 'pointer',
                        background: dragOver ? 'rgba(var(--accent-light-rgb),0.08)' : 'rgba(255,255,255,0.02)',
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
                        style={{ color: dragOver ? 'var(--accent-light)' : 'var(--text-muted)', margin: '0 auto 8px' }}>
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
                {selectedFile && !result && !progress && (
                    <button
                        onClick={handleUpload}
                        disabled={loading}
                        className="w-full px-4 py-3 rounded-lg text-sm font-semibold flex items-center justify-center gap-2"
                        style={{
                            background: loading ? 'rgba(var(--accent-rgb),0.5)' : 'linear-gradient(135deg, var(--accent), var(--accent-light))',
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
                        {loading ? 'Başlatılıyor…' : 'Yükle ve İçeri Aktar'}
                    </button>
                )}

                {/* Progress — streaming sırasında per-row ilerleme */}
                {progress && !result && (
                    <div className="rounded-lg p-4 space-y-3"
                        style={{ background: 'rgba(var(--accent-rgb),0.08)', border: '1px solid rgba(var(--accent-rgb),0.25)' }}>
                        <div className="flex items-center justify-between">
                            <div className="flex items-center gap-2">
                                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                                    strokeWidth="2.5" className="animate-spin" style={{ color: 'var(--accent-light)' }}>
                                    <path d="M21 12a9 9 0 1 1-6.219-8.56" />
                                </svg>
                                <span className="text-sm font-medium" style={{ color: '#e9e5ff' }}>
                                    İşleniyor — {progress.processed}/{progress.total || '?'}
                                </span>
                            </div>
                            <span className="text-xs" style={{ color: 'var(--gray-light)' }}>
                                {progress.total > 0 ? `%${Math.round((progress.processed / progress.total) * 100)}` : ''}
                            </span>
                        </div>

                        {/* Progress bar */}
                        <div style={{ height: '8px', background: 'rgba(255,255,255,0.06)', borderRadius: '999px', overflow: 'hidden' }}>
                            <div
                                style={{
                                    height: '100%',
                                    width: progress.total > 0 ? `${Math.min(100, (progress.processed / progress.total) * 100)}%` : '0%',
                                    background: 'linear-gradient(90deg, var(--accent), var(--accent-light))',
                                    transition: 'width 0.2s ease-out',
                                }}
                            />
                        </div>

                        {/* Canlı sayaçlar */}
                        <div className="flex gap-2 text-xs">
                            <div style={{ color: '#86efac' }}>✓ {progress.successCount} başarılı</div>
                            <div style={{ color: 'var(--gray-light)' }}>•</div>
                            <div style={{ color: '#fb923c' }}>⚠ {progress.skippedCount} atlandı</div>
                            {progress.lastEmail && (
                                <>
                                    <div style={{ color: 'var(--gray-light)' }}>•</div>
                                    <div style={{ color: 'var(--gray-light)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: '200px' }}
                                        title={progress.lastEmail}>
                                        Son: {progress.lastEmail}
                                    </div>
                                </>
                            )}
                        </div>
                    </div>
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
                            style={{ border: '1px solid rgba(255,255,255,0.08)', maxHeight: '300px', overflowY: 'auto', overflowX: 'auto' }}>
                            <table className="w-full text-xs">
                                {/* Sticky başlık OPAK zeminli olmalı — yarı saydam olursa kaydırmada
                                    altından geçen satırlar görünür ("iç içe binme"). zIndex: satır
                                    içeriklerinin üstünde kalsın. */}
                                <thead style={{ background: '#221c3a', position: 'sticky', top: 0, zIndex: 1 }}>
                                    <tr>
                                        <th className="px-3 py-2 text-left" style={{ color: 'var(--text-primary)', fontWeight: 600 }}>Satır</th>
                                        <th className="px-3 py-2 text-left" style={{ color: 'var(--text-primary)', fontWeight: 600 }}>E-posta</th>
                                        <th className="px-3 py-2 text-left" style={{ color: 'var(--text-primary)', fontWeight: 600 }}>Durum</th>
                                        <th className="px-3 py-2 text-left" style={{ color: 'var(--text-primary)', fontWeight: 600 }}>Sebep</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {result.results.map((r, i) => (
                                        <tr key={i} style={{ borderTop: '1px solid rgba(255,255,255,0.05)' }}>
                                            <td className="px-3 py-2" style={{ color: 'var(--text-muted)' }}>{r.row}</td>
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
