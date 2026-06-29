import { motion } from 'framer-motion';
import SparklesCore from '../shared/SparklesCore';
import TextType from '../shared/TextType';

export default function AuthCard({ children }) {
    return (
        <div
            className="relative w-full min-h-screen flex flex-col items-center justify-center overflow-hidden"
            style={{ background: '#000' }}
        >
            {/* Başlık */}
            <motion.h1
                initial={{ opacity: 0, y: -10 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.6, ease: 'easeOut' }}
                style={{
                    fontSize: 'clamp(48px, 8vw, 96px)',
                    fontWeight: 800,
                    color: '#fff',
                    letterSpacing: '-0.03em',
                    lineHeight: 1,
                    margin: 0,
                    textAlign: 'center',
                    position: 'relative',
                    zIndex: 20,
                }}
            >
                DocuChat
            </motion.h1>

            {/* Gradient ışın çizgileri (horizon) */}
            <div style={{ position: 'relative', width: 'min(640px, 90vw)', height: '40px', marginTop: '8px', zIndex: 20 }}>
                {/* Kısa parlak çizgi */}
                <div style={{ position: 'absolute', inset: 'auto 0 12px 0', left: '50%', transform: 'translateX(-50%)', height: '2px', width: '75%', background: 'linear-gradient(to right, transparent, var(--accent-deep), transparent)', filter: 'blur(1px)' }} />
                <div style={{ position: 'absolute', inset: 'auto 0 12px 0', left: '50%', transform: 'translateX(-50%)', height: '1px', width: '75%', background: 'linear-gradient(to right, transparent, #818cf8, transparent)' }} />
                {/* Uzun ince çizgi */}
                <div style={{ position: 'absolute', inset: 'auto 0 12px 0', left: '50%', transform: 'translateX(-50%)', height: '5px', width: '25%', background: 'linear-gradient(to right, transparent, #06b6d4, transparent)', filter: 'blur(3px)' }} />
                <div style={{ position: 'absolute', inset: 'auto 0 12px 0', left: '50%', transform: 'translateX(-50%)', height: '1px', width: '25%', background: 'linear-gradient(to right, transparent, #22d3ee, transparent)' }} />
            </div>

            {/* Sparkles bölgesi (başlığın altı) */}
            <div
                aria-hidden
                style={{
                    position: 'absolute',
                    left: 0,
                    right: 0,
                    top: '50%',
                    height: '50%',
                    pointerEvents: 'none',
                    zIndex: 1,
                }}
            >
                <SparklesCore
                    id="auth-sparkles"
                    background="transparent"
                    minSize={0.4}
                    maxSize={1.0}
                    particleDensity={1200}
                    speed={1}
                    particleColor="#ffffff"
                    className="w-full h-full"
                />

                {/* Radial mask — kenarları yumuşat */}
                <div
                    style={{
                        position: 'absolute',
                        inset: 0,
                        background:
                            'radial-gradient(ellipse 80% 100% at 50% 0%, transparent 10%, #000 75%)',
                    }}
                />
            </div>

            {/* TextType alt yazı */}
            <div
                style={{
                    color: '#94a3b8',
                    fontSize: '15px',
                    marginTop: '20px',
                    minHeight: '24px',
                    position: 'relative',
                    zIndex: 20,
                    textAlign: 'center',
                }}
            >
                <TextType
                    text={[
                        "DocuChat'e hoş geldiniz",
                        'Belgelerinizle konuşun',
                        'Sorularınızı saniyeler içinde yanıtlayın',
                    ]}
                    typingSpeed={65}
                    deletingSpeed={35}
                    pauseDuration={2000}
                    cursorCharacter="_"
                    cursorBlinkDuration={0.55}
                />
            </div>

            {/* Login card */}
            <motion.div
                initial={{ scale: 0.96, opacity: 0, y: 16 }}
                animate={{ scale: 1, opacity: 1, y: 0 }}
                transition={{ duration: 0.5, delay: 0.15, ease: 'easeOut' }}
                className="w-full max-w-md px-4"
                style={{ position: 'relative', zIndex: 30, marginTop: '32px' }}
            >
                <div
                    style={{
                        background: 'rgba(17, 17, 23, 0.7)',
                        backdropFilter: 'blur(16px) saturate(150%)',
                        WebkitBackdropFilter: 'blur(16px) saturate(150%)',
                        border: '1px solid rgba(255,255,255,0.08)',
                        borderRadius: '20px',
                        padding: '32px',
                        boxShadow:
                            '0 30px 80px -20px rgba(0,0,0,0.8), 0 0 0 1px rgba(255,255,255,0.04), inset 0 1px 0 rgba(255,255,255,0.05)',
                    }}
                >
                    {children}
                </div>
            </motion.div>
        </div>
    );
}
