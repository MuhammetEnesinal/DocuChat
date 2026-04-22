export default function Logo({ size = 'md' }) {
    const sizes = {
        sm: { box: 'w-8 h-8', svg: 16, text: 'text-base' },
        md: { box: 'w-14 h-14', svg: 28, text: 'text-3xl' },
    };
    const s = sizes[size] ?? sizes.md;

    return (
        <div className="flex flex-col items-center gap-0">
            <div className={`inline-flex items-center justify-center ${s.box} rounded-2xl mb-2`}
                style={{ background: 'var(--accent)', boxShadow: '0 0 30px rgba(59,130,246,0.3)' }}>
                <svg width={s.svg} height={s.svg} viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2">
                    <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                </svg>
            </div>
            <h1 className={`${s.text} font-bold text-white tracking-tight`}>DocuChat</h1>
            <p className="text-sm mt-1" style={{ color: 'var(--gray-light)' }}>Kurumsal Doküman Asistanı</p>
        </div>
    );
}