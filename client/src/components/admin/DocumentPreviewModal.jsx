import { useState, useEffect, useRef } from 'react';
import api from '../../services/api';
import { formatSize } from '../../utils/format';
import { useToast } from '../shared/Toast';
import Spinner from '../shared/Spinner';

export default function DocumentPreviewModal({ doc, onClose }) {
    const [blobUrl, setBlobUrl] = useState(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const blobUrlRef = useRef(null);
    const toast = useToast();

    const ext = doc.fileName.split('.').pop().toLowerCase();
    const isPdf = doc.contentType === 'application/pdf' || ext === 'pdf';
    const isImage = ['png', 'jpg', 'jpeg', 'gif', 'bmp', 'webp'].includes(ext);
    const canPreview = isPdf || isImage;

    useEffect(() => {
        if (!canPreview) { setLoading(false); return; }
        let cancelled = false;
        (async () => {
            try {
                const res = await api.get(`/documents/${doc.id}/preview`, { responseType: 'blob' });
                if (cancelled) return;
                const url = URL.createObjectURL(res.data);
                blobUrlRef.current = url;
                setBlobUrl(url);
            } catch (e) {
                if (!cancelled) {
                    const status = e?.response?.status;
                    if (status === 404) setError('Dosya fiziksel olarak bulunamadı. Belgeyi yeniden yükleyin.');
                    else if (status === 401) setError('Bu dosyayı görüntüleme yetkiniz yok.');
                    else setError(`Dosya yüklenemedi (${status ?? e.message})`);
                }
            } finally {
                if (!cancelled) setLoading(false);
            }
        })();
        return () => {
            cancelled = true;
            if (blobUrlRef.current) { URL.revokeObjectURL(blobUrlRef.current); blobUrlRef.current = null; }
        };
    }, [doc.id, canPreview]);

    useEffect(() => {
        const h = (e) => { if (e.key === 'Escape') onClose(); };
        window.addEventListener('keydown', h);
        return () => window.removeEventListener('keydown', h);
    }, [onClose]);

    const handleDownload = async () => {
        try {
            const res = await api.get(`/documents/${doc.id}/preview`, { responseType: 'blob' });
            const url = URL.createObjectURL(res.data);
            const a = document.createElement('a');
            a.href = url; a.download = doc.fileName; a.click();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        } catch (e) {
            console.error('İndirme hatası:', e);
            toast.error('Dosya indirilemedi. Lütfen tekrar deneyin.');
        }
    };

    return (
        <div onClick={(e) => { if (e.target === e.currentTarget) onClose(); }} style={{
            position: 'fixed', inset: 0, zIndex: 1000,
            background: 'rgba(0,0,0,0.75)', backdropFilter: 'blur(4px)',
            display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '24px',
        }}>
            <div style={{
                background: '#0e0a1c', border: '1px solid rgba(167,139,250,0.18)',
                borderRadius: '16px', width: '100%', maxWidth: '900px',
                maxHeight: '90vh', display: 'flex', flexDirection: 'column', overflow: 'hidden',
                boxShadow: '0 30px 80px -20px rgba(0,0,0,0.7), 0 0 40px -10px rgba(139,92,246,0.2)',
            }}>
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '16px 20px', borderBottom: '1px solid var(--border)', flexShrink: 0 }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px', minWidth: 0 }}>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#60a5fa" strokeWidth="2">
                            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" />
                        </svg>
                        <span style={{ fontSize: '14px', fontWeight: 600, color: 'var(--text-primary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{doc.fileName}</span>
                        <span style={{ fontSize: '12px', color: 'var(--gray-light)', flexShrink: 0 }}>{formatSize(doc.fileSizeBytes)}</span>
                    </div>
                    <div style={{ display: 'flex', gap: '8px', flexShrink: 0 }}>
                        <button onClick={handleDownload} style={{ padding: '6px 12px', borderRadius: '8px', fontSize: '13px', fontWeight: 500, background: 'rgba(96,165,250,0.1)', color: '#60a5fa', border: '1px solid rgba(96,165,250,0.2)', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px' }}>
                            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" /><polyline points="7 10 12 15 17 10" /><line x1="12" y1="15" x2="12" y2="3" /></svg>
                            İndir
                        </button>
                        <button onClick={onClose} aria-label="Kapat" style={{ padding: '6px', borderRadius: '8px', background: 'none', border: '1px solid var(--border)', cursor: 'pointer', color: 'var(--text-muted)', display: 'flex', alignItems: 'center' }}>
                            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
                        </button>
                    </div>
                </div>

                <div style={{ flex: 1, overflow: 'hidden', display: 'flex', minHeight: '400px' }}>
                    {loading ? (
                        <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '12px', color: 'var(--gray-light)' }}>
                            <Spinner size={20} />
                            Yükleniyor...
                        </div>
                    ) : error ? (
                        <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#f87171', fontSize: '14px' }}>
                            Dosya yüklenemedi: {error}
                        </div>
                    ) : !canPreview ? (
                        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', padding: '48px 24px', gap: '16px' }}>
                            <div style={{ width: '64px', height: '64px', borderRadius: '16px', background: 'rgba(96,165,250,0.1)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="#60a5fa" strokeWidth="1.5"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" /></svg>
                            </div>
                            <div style={{ textAlign: 'center' }}>
                                <p style={{ fontSize: '15px', fontWeight: 600, color: 'var(--text-primary)', marginBottom: '8px' }}>{doc.fileName}</p>
                                <p style={{ fontSize: '13px', color: 'var(--gray-light)', marginBottom: '20px' }}>Bu dosya türü tarayıcıda önizlenemiyor.</p>
                                <button onClick={handleDownload} style={{ padding: '10px 20px', borderRadius: '10px', fontSize: '14px', fontWeight: 500, background: 'var(--accent)', color: 'var(--text-primary)', border: 'none', cursor: 'pointer', display: 'inline-flex', alignItems: 'center', gap: '8px' }}>
                                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" /><polyline points="7 10 12 15 17 10" /><line x1="12" y1="15" x2="12" y2="3" /></svg>
                                    İndir
                                </button>
                            </div>
                        </div>
                    ) : isPdf ? (
                        <iframe src={blobUrl} style={{ width: '100%', height: '100%', border: 'none', minHeight: '600px' }} title={doc.fileName} />
                    ) : (
                        <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '24px', overflow: 'auto' }}>
                            <img src={blobUrl} alt={doc.fileName} style={{ maxWidth: '100%', maxHeight: '70vh', objectFit: 'contain', borderRadius: '8px' }} />
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
}
