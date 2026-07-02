import { motion } from 'framer-motion';

export default function NewChatHero({ children, popularQuestions, onSelectQuestion }) {
    // Yalnızca backend'den gerçek popüler sorular geldiğinde gösterilir. Hiç soru yoksa bölüm
    // tamamen gizlenir; sabit/uydurma bir liste gösterilmez (kullanıcıyı yanıltmamak için).
    const hasRealPopular = popularQuestions && popularQuestions.length > 0;
    const chips = hasRealPopular ? popularQuestions.slice(0, 6) : [];

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
                    color: 'var(--text-secondary)',
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

                {/* En Çok Sorulan Sorular — sadece backend gerçek veri dönüyorsa göster */}
                {hasRealPopular && (
                    <div style={{ width: '100%', marginTop: '32px' }}>
                        <p style={{
                            fontSize: '11px',
                            fontWeight: 600,
                            letterSpacing: '0.14em',
                            textTransform: 'uppercase',
                            color: 'var(--text-secondary)',
                            textAlign: 'center',
                            margin: '0 0 14px',
                        }}>
                            En Çok Sorulan Sorular
                        </p>
                        <div style={{
                            display: 'grid',
                            // Geniş ekranda 2 kolon; kap 280px'in altına düşünce tek kolona iner —
                            // çipler asla harf harf kırılacak kadar daralmaz.
                            gridTemplateColumns: 'repeat(auto-fit, minmax(min(280px, 100%), 1fr))',
                            gap: '10px',
                            width: '100%',
                        }}>
                            {chips.map((label, i) => (
                                <motion.button
                                    key={i}
                                    onClick={() => onSelectQuestion?.(label)}
                                    initial={{ opacity: 0, y: 8 }}
                                    animate={{ opacity: 1, y: 0 }}
                                    transition={{ duration: 0.35, delay: 0.15 + i * 0.04, ease: 'easeOut' }}
                                    whileHover={{ y: -2 }}
                                    title={label}
                                    style={{
                                        display: 'flex', alignItems: 'flex-start', gap: '10px',
                                        padding: '11px 14px',
                                        borderRadius: '12px',
                                        // Koyu cam (input kutusuyla aynı aile): arkadaki ışık içinden geçip çipi patlatmaz
                                        background: 'rgba(32, 28, 56, 0.6)',
                                        backdropFilter: 'blur(20px) saturate(130%)',
                                        WebkitBackdropFilter: 'blur(20px) saturate(130%)',
                                        border: '1px solid rgba(var(--accent-light-rgb),0.22)',
                                        color: 'var(--text-secondary)',
                                        fontSize: '13px',
                                        fontWeight: 500,
                                        cursor: 'pointer',
                                        transition: 'all 0.2s',
                                        textAlign: 'left',
                                        width: '100%',
                                        minWidth: 0,
                                        boxShadow: '0 8px 22px -10px rgba(0,0,0,0.5), inset 0 1px 0 rgba(255,255,255,0.05)',
                                    }}
                                    onMouseEnter={(e) => {
                                        e.currentTarget.style.background = 'rgba(46, 40, 76, 0.75)';
                                        e.currentTarget.style.borderColor = 'rgba(var(--accent-light-rgb),0.45)';
                                        e.currentTarget.style.color = '#fff';
                                    }}
                                    onMouseLeave={(e) => {
                                        e.currentTarget.style.background = 'rgba(32, 28, 56, 0.6)';
                                        e.currentTarget.style.borderColor = 'rgba(var(--accent-light-rgb),0.22)';
                                        e.currentTarget.style.color = 'var(--text-secondary)';
                                    }}
                                >
                                    <span style={{
                                        minWidth: 0, flex: 1, lineHeight: 1.4,
                                        // Uzun soru dikine uzamaz: en fazla 2 satır, fazlası "..." ile kesilir
                                        display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical',
                                        overflow: 'hidden', textOverflow: 'ellipsis',
                                    }}>{label}</span>
                                </motion.button>
                            ))}
                        </div>
                    </div>
                )}
            </motion.div>
            <div style={{ flex: 1 }} />
        </div>
    );
}
