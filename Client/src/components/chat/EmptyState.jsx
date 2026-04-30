export default function EmptyState({ popularQuestions, onSelectQuestion }) {
    const hints = ['Bu belgeler ne hakkında?', 'Özet çıkar', 'Önemli maddeleri listele', 'Daha fazla anlat'];

    return (
        <div className="flex flex-col items-center justify-center h-full text-center py-20 px-4">
            <div className="w-16 h-16 rounded-2xl flex items-center justify-center mb-5"
                style={{ background: 'var(--surface2)', border: '1px solid var(--border)' }}>
                <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="#3b82f6" strokeWidth="1.5">
                    <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                </svg>
            </div>
            <h2 className="text-lg font-semibold mb-2" style={{ color: "var(--text-primary)" }}>Belgeleri sorgulayın</h2>
            <p className="text-sm max-w-sm mb-6" style={{ color: 'var(--gray-light)' }}>
                Yüklü belgeler hakkında sorularınızı sorun.
            </p>

            {popularQuestions.length > 0 ? (
                <div className="w-full max-w-lg">
                    <p className="text-xs font-medium mb-3 text-center" style={{ color: 'var(--text-secondary)' }}>
                        EN ÇOK SORULAN SORULAR
                    </p>
                    <div className="grid grid-cols-1 gap-2">
                        {popularQuestions.map((q, i) => (
                            <button key={i} onClick={() => onSelectQuestion(q)}
                                className="px-4 py-3 rounded-xl text-sm text-left transition-all flex items-center gap-3"
                                style={{ background: 'var(--surface2)', border: '1px solid var(--border)', color: 'var(--text-muted)' }}
                                onMouseEnter={(e) => { e.currentTarget.style.borderColor = 'var(--accent)'; e.currentTarget.style.color = 'var(--text-primary)'; e.currentTarget.style.background = 'rgba(59,130,246,0.05)'; }}
                                onMouseLeave={(e) => { e.currentTarget.style.borderColor = 'var(--border)'; e.currentTarget.style.color = 'var(--text-muted)'; e.currentTarget.style.background = 'var(--surface2)'; }}>
                                <span className="text-xs px-1.5 py-0.5 rounded flex-shrink-0 font-medium"
                                    style={{ background: 'rgba(59,130,246,0.15)', color: 'var(--accent-light)' }}>
                                    #{i + 1}
                                </span>
                                <span className="truncate">{q}</span>
                            </button>
                        ))}
                    </div>
                </div>
            ) : (
                <div className="grid grid-cols-2 gap-2 w-full max-w-md">
                    {hints.map((hint) => (
                        <button key={hint} onClick={() => onSelectQuestion(hint)}
                            className="px-3 py-2.5 rounded-xl text-xs text-left transition-all"
                            style={{ background: 'var(--surface2)', border: '1px solid var(--border)', color: 'var(--text-muted)' }}
                            onMouseEnter={(e) => { e.currentTarget.style.borderColor = 'var(--accent)'; e.currentTarget.style.color = 'var(--text-primary)'; }}
                            onMouseLeave={(e) => { e.currentTarget.style.borderColor = 'var(--border)'; e.currentTarget.style.color = 'var(--text-muted)'; }}>
                            {hint}
                        </button>
                    ))}
                </div>
            )}
        </div>
    );
}