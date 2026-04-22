import { useNavigate } from 'react-router-dom';
import { useAuth } from '../hooks/useAuth';

export default function NotFound() {
    const navigate = useNavigate();
    const { homeRoute } = useAuth();

    return (
        <div className="min-h-screen flex items-center justify-center" style={{ background: 'var(--navy)' }}>
            <div className="text-center px-4">
                <div className="inline-flex items-center justify-center w-20 h-20 rounded-2xl mb-6"
                    style={{ background: 'var(--surface)', border: '1px solid var(--border)' }}>
                    <svg width="36" height="36" viewBox="0 0 24 24" fill="none" stroke="#3b82f6" strokeWidth="1.5">
                        <circle cx="12" cy="12" r="10" />
                        <line x1="12" y1="8" x2="12" y2="12" />
                        <line x1="12" y1="16" x2="12.01" y2="16" />
                    </svg>
                </div>
                <h1 className="text-6xl font-bold mb-3" style={{ color: 'var(--accent)' }}>404</h1>
                <p className="text-lg font-medium text-white mb-2">Sayfa bulunamadı</p>
                <p className="text-sm mb-8" style={{ color: 'var(--gray-light)' }}>
                    Aradığınız sayfa mevcut değil veya taşınmış olabilir.
                </p>
                <button
                    onClick={() => navigate(homeRoute)}
                    className="px-6 py-3 rounded-xl text-sm font-medium text-white transition-all"
                    style={{ background: 'var(--accent)' }}
                    onMouseEnter={(e) => e.currentTarget.style.opacity = '0.85'}
                    onMouseLeave={(e) => e.currentTarget.style.opacity = '1'}
                >
                    Ana Sayfaya Dön
                </button>
            </div>
        </div>
    );
}