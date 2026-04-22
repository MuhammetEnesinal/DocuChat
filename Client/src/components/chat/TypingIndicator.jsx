export default function TypingIndicator() {
    return (
        <div style={{ display: 'flex', justifyContent: 'flex-start', alignItems: 'flex-start', gap: '12px' }}>
            <div style={{ width: '32px', height: '32px', borderRadius: '8px', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0, background: 'var(--accent)' }}>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
                    <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                </svg>
            </div>
            <div style={{ padding: '12px 16px', borderRadius: '16px', borderTopLeftRadius: '4px', background: 'var(--surface2)', border: '1px solid var(--border)' }}>
                <div style={{ display: 'flex', gap: '4px', alignItems: 'center' }}>
                    {[0, 150, 300].map((delay) => (
                        <div key={delay} style={{
                            width: '8px', height: '8px', borderRadius: '50%', background: 'var(--accent)',
                            animation: 'bounce 1s infinite',
                            animationDelay: `${delay}ms`,
                        }} />
                    ))}
                </div>
            </div>
        </div>
    );
}