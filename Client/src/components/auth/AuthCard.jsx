import Logo from '../shared/Logo';

export default function AuthCard({ children }) {
    return (
        <div className="min-h-screen flex items-center justify-center" style={{ background: 'var(--navy)' }}>
            <div className="w-full max-w-md px-4">
                <div className="text-center mb-10">
                    <Logo size="md" />
                </div>
                <div className="rounded-2xl p-8" style={{ background: 'var(--surface)', border: '1px solid var(--border)' }}>
                    {children}
                </div>
            </div>
        </div>
    );
}
