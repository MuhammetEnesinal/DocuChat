export default function Logo({ size = 'md' }) {
    const sizes = {
        sm: { box: 'w-9 h-9', svg: 18, text: 'text-base' },
        md: { box: 'w-16 h-16', svg: 30, text: 'text-3xl' },
    };
    const s = sizes[size] ?? sizes.md;

    return (
        <div className="flex flex-col items-center gap-0">
            <div className={`inline-flex items-center justify-center ${s.box} rounded-2xl mb-3 relative`}
                style={{
                    background: 'var(--gradient-accent)',
                    boxShadow: '0 0 40px rgba(99,102,241,0.45), 0 10px 30px -10px rgba(99,102,241,0.6), inset 0 1px 0 rgba(255,255,255,0.2)',
                }}>
                <svg width={s.svg} height={s.svg} viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                </svg>
            </div>
            <h1 className={`${s.text} font-bold tracking-tight gradient-text`}>DocuChat</h1>
            <p className="text-sm mt-1.5" style={{ color: 'var(--text-muted)' }}>Kurumsal Doküman Asistanı</p>
        </div>
    );
}
