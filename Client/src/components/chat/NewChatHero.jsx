import { motion } from 'framer-motion';

const QUICK_ACTIONS = [
    { label: 'Bu belgeler ne hakkında?', icon: <><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" /></> },
    { label: 'Özet çıkar', icon: <><line x1="8" y1="6" x2="21" y2="6" /><line x1="8" y1="12" x2="21" y2="12" /><line x1="8" y1="18" x2="21" y2="18" /><line x1="3" y1="6" x2="3.01" y2="6" /><line x1="3" y1="12" x2="3.01" y2="12" /><line x1="3" y1="18" x2="3.01" y2="18" /></> },
    { label: 'Önemli maddeleri listele', icon: <><polyline points="9 11 12 14 22 4" /><path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11" /></> },
    { label: 'Detaylı anlat', icon: <><circle cx="11" cy="11" r="8" /><line x1="21" y1="21" x2="16.65" y2="16.65" /></> },
    { label: 'Tablo halinde göster', icon: <><rect x="3" y="3" width="18" height="18" rx="2" /><line x1="3" y1="9" x2="21" y2="9" /><line x1="3" y1="15" x2="21" y2="15" /><line x1="9" y1="3" x2="9" y2="21" /><line x1="15" y1="3" x2="15" y2="21" /></> },
    { label: 'Karşılaştır', icon: <><polyline points="17 1 21 5 17 9" /><path d="M3 11V9a4 4 0 0 1 4-4h14" /><polyline points="7 23 3 19 7 15" /><path d="M21 13v2a4 4 0 0 1-4 4H3" /></> },
];

export default function NewChatHero({ children, popularQuestions, onSelectQuestion }) {
    const chips = popularQuestions?.length > 0
        ? popularQuestions.slice(0, 6).map((q, i) => ({ label: q, icon: QUICK_ACTIONS[i % QUICK_ACTIONS.length].icon }))
        : QUICK_ACTIONS;

    return (
        <div className="violet-dome" style={{ width: '100%', height: '100%', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', padding: '24px', overflowY: 'auto' }}>
            <div style={{ flex: 1 }} />
            <motion.div
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.6, ease: 'easeOut' }}
                style={{ width: '100%', maxWidth: '720px', display: 'flex', flexDirection: 'column', alignItems: 'center', flexShrink: 0 }}
            >
                {/* Başlık */}
                <h1 style={{
                    fontSize: 'clamp(40px, 5vw, 56px)',
                    fontWeight: 600,
                    color: '#fff',
                    letterSpacing: '-0.02em',
                    lineHeight: 1.1,
                    margin: 0,
                    textAlign: 'center',
                }}>
                    DocuChat
                </h1>
                <p style={{
                    fontSize: '15px',
                    color: 'rgba(255,255,255,0.6)',
                    marginTop: '14px',
                    marginBottom: '40px',
                    textAlign: 'center',
                    maxWidth: '520px',
                }}>
                    Sormak istediğiniz soruyu aşağıdan yazarak başlayabilirsiniz.
                </p>

                {/* Input (parent'tan geliyor) */}
                <div style={{ width: '100%' }}>
                    {children}
                </div>

                {/* Quick action chips */}
                <div style={{ width: '100%', marginTop: '32px' }}>
                    <p style={{
                        fontSize: '11px',
                        fontWeight: 600,
                        letterSpacing: '0.14em',
                        textTransform: 'uppercase',
                        color: 'rgba(255,255,255,0.45)',
                        textAlign: 'center',
                        margin: '0 0 14px',
                    }}>
                        En Çok Sorulan Sorular
                    </p>
                    <div style={{
                        display: 'grid',
                        gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
                        gap: '10px',
                        width: '100%',
                    }}>
                        {chips.slice(0, 6).map((chip, i) => (
                            <motion.button
                                key={i}
                                onClick={() => onSelectQuestion?.(chip.label)}
                                initial={{ opacity: 0, y: 8 }}
                                animate={{ opacity: 1, y: 0 }}
                                transition={{ duration: 0.35, delay: 0.15 + i * 0.04, ease: 'easeOut' }}
                                whileHover={{ y: -2 }}
                                title={chip.label}
                                style={{
                                    display: 'flex', alignItems: 'flex-start', gap: '10px',
                                    padding: '11px 14px',
                                    borderRadius: '12px',
                                    background: 'linear-gradient(135deg, rgba(139,92,246,0.16) 0%, rgba(99,102,241,0.12) 100%)',
                                    backdropFilter: 'blur(20px) saturate(180%)',
                                    WebkitBackdropFilter: 'blur(20px) saturate(180%)',
                                    border: '1px solid rgba(167,139,250,0.25)',
                                    color: '#e9e5ff',
                                    fontSize: '13px',
                                    fontWeight: 500,
                                    cursor: 'pointer',
                                    transition: 'all 0.2s',
                                    textAlign: 'left',
                                    width: '100%',
                                    minWidth: 0,
                                    boxShadow: '0 4px 14px -6px rgba(139,92,246,0.28), inset 0 1px 0 rgba(255,255,255,0.05)',
                                }}
                                onMouseEnter={(e) => {
                                    e.currentTarget.style.background = 'linear-gradient(135deg, rgba(139,92,246,0.30) 0%, rgba(99,102,241,0.22) 100%)';
                                    e.currentTarget.style.borderColor = 'rgba(167,139,250,0.5)';
                                    e.currentTarget.style.color = '#fff';
                                }}
                                onMouseLeave={(e) => {
                                    e.currentTarget.style.background = 'linear-gradient(135deg, rgba(139,92,246,0.16) 0%, rgba(99,102,241,0.12) 100%)';
                                    e.currentTarget.style.borderColor = 'rgba(167,139,250,0.25)';
                                    e.currentTarget.style.color = '#e9e5ff';
                                }}
                            >
                                <span style={{ minWidth: 0, flex: 1, lineHeight: 1.4, wordBreak: 'break-word' }}>{chip.label}</span>
                            </motion.button>
                        ))}
                    </div>
                </div>
            </motion.div>
            <div style={{ flex: 1 }} />
        </div>
    );
}
