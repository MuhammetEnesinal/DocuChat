import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { Light as SyntaxHighlighter } from 'react-syntax-highlighter';
import { atomOneDark } from 'react-syntax-highlighter/dist/esm/styles/hljs';
import { useState } from 'react';
import { API_BASE, submitFeedback } from '../../services/api';
import FeedbackModal from './FeedbackModal';

function formatTime(dateStr) {
    if (!dateStr) return '';
    return new Date(dateStr).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' });
}

/**
 * LLM bazen image syntax'ını bozuk üretir — markdown parser parse edemez, raw `![alt](` ve `)`
 * ekranda görünür. İki tipik bozulma:
 *   1. Nested:     ![alt]( ![alt](/uploads/img.png) )       → outer wrap atılır
 *   2. Multi-line: ![alt](\n /uploads/img.png \n)            → URL içindeki whitespace temizlenir
 *   3. URL'de boşluk:  ![alt]( /uploads/img.png )            → trim
 * Sıra önemli: nested önce, sonra whitespace.
 */
function normalizeImageMarkdown(content) {
    if (!content) return content;
    let out = content;
    // [1] Nested image wrap: outer ![..]( <inner image> ) → sadece inner
    // Iteratif uygula — birden fazla katman olabilir.
    const nestedRe = /!\[[^\]]*\]\(\s*(!\[[^\]]*\]\([^)]+\))\s*\)/g;
    let safety = 5;
    while (nestedRe.test(out) && safety-- > 0) {
        out = out.replace(nestedRe, '$1');
    }
    // [2] URL içindeki whitespace/newline'ı temizle: ![alt](  url  ) → ![alt](url)
    // Sadece kapanış parantezi ile aynı satırda olmasa bile yakalar (s flag ile newline match).
    out = out.replace(
        /!\[([^\]]*)\]\(\s*([^)]+?)\s*\)/gs,
        (_m, alt, url) => `![${alt}](${url.replace(/\s+/g, '')})`
    );
    return out;
}

/**
 * Streaming sırasında YARIM markdown image syntax'ı gizler:
 *   ![alt](url... → (henüz `)` gelmedi) → kullanıcı çirkin text görmesin
 *
 * Mantık: stream içinde son yarım kalmış `![...]( ...` parçasını cevabın sonundan kırp.
 * Stream bittikten sonra TÜM cevap zaten tamamlanmış olur, normal render edilir.
 * Tamamlanmış görseller (kapanış `)` olan) etkilenmez — onlar normal render olur.
 */
function sanitizeStreamingMarkdown(content, isStreaming) {
    if (!content) return content;
    // Önce LLM bozulmalarını düzelt (her durumda — streaming dahil)
    let out = normalizeImageMarkdown(content);
    if (!isStreaming) return out;
    // Sondaki açık alt ya da açık url parçasını kes
    return out.replace(/!\[[^\]]*(?:\][^()]*(?:\([^)]*)?)?$/, '');
}

function ImageModal({ src, onClose }) {
    return (
        <div
            onClick={onClose}
            style={{
                position: 'fixed', inset: 0, zIndex: 1000,
                background: 'rgba(0,0,0,0.85)',
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                cursor: 'zoom-out',
            }}
        >
            <div style={{ position: 'relative', display: 'inline-flex' }} onClick={e => e.stopPropagation()}>
                <img
                    src={src}
                    alt="büyük görsel"
                    style={{
                        display: 'block',
                        width: 'auto',
                        height: 'auto',
                        maxWidth: '90vw',
                        maxHeight: '90vh',
                        objectFit: 'contain',
                        borderRadius: '12px',
                        boxShadow: '0 25px 60px rgba(0,0,0,0.6)',
                    }}
                />
                <button
                    onClick={onClose}
                    style={{
                        position: 'absolute', top: '-14px', right: '-14px',
                        width: '32px', height: '32px',
                        background: 'rgba(255,255,255,0.2)',
                        backdropFilter: 'blur(4px)',
                        border: '1px solid rgba(255,255,255,0.3)',
                        color: 'white', borderRadius: '50%',
                        fontSize: '18px', lineHeight: 1,
                        cursor: 'pointer',
                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                        flexShrink: 0,
                    }}
                >×</button>
            </div>
        </div>
    );
}

function ClarificationBubble({ msg, onClarificationSelect, onClarificationDismiss }) {
    return (
        <div style={{ display: 'flex', justifyContent: 'flex-start', alignItems: 'flex-start', gap: '12px' }}>
            <div style={{ width: '34px', height: '34px', borderRadius: '10px', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0, marginTop: '4px', background: 'var(--gradient-accent)', boxShadow: '0 6px 18px -6px rgba(var(--accent-rgb),0.6)' }}>
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
                    <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                </svg>
            </div>
            <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ background: 'var(--surface2)', border: '1px solid var(--border)', borderRadius: '16px', borderTopLeftRadius: '4px', padding: '14px 16px' }}>
                    <p style={{ fontSize: '13px', color: 'var(--text-muted)', margin: '0 0 10px 0' }}>Bunu mu demek istediniz?</p>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                        {msg.clarificationOptions.map((opt, i) => (
                            <button key={i} onClick={() => onClarificationSelect(opt)}
                                style={{ textAlign: 'left', padding: '9px 13px', borderRadius: '8px', background: 'var(--surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', cursor: 'pointer', fontSize: '13px', transition: 'border-color 0.15s, background 0.15s' }}
                                onMouseEnter={e => { e.currentTarget.style.borderColor = 'var(--accent)'; e.currentTarget.style.background = 'var(--surface2)'; }}
                                onMouseLeave={e => { e.currentTarget.style.borderColor = 'var(--border)'; e.currentTarget.style.background = 'var(--surface)'; }}>
                                {opt}
                            </button>
                        ))}
                    </div>
                    <button
                        onClick={() => onClarificationDismiss(msg.id)}
                        style={{ marginTop: '10px', padding: '7px 13px', borderRadius: '8px', background: 'transparent', border: '1px solid var(--border)', color: 'var(--text-muted)', cursor: 'pointer', fontSize: '12px', width: '100%', textAlign: 'center' }}
                        onMouseEnter={e => { e.currentTarget.style.borderColor = '#ef4444'; e.currentTarget.style.color = '#ef4444'; }}
                        onMouseLeave={e => { e.currentTarget.style.borderColor = 'var(--border)'; e.currentTarget.style.color = 'var(--text-muted)'; }}>
                        Hayır, kendi cümlemle sormak istiyorum
                    </button>
                </div>
                <span style={{ fontSize: '12px', color: '#475569', marginTop: '4px', display: 'block' }}>{formatTime(msg.createdAt)}</span>
            </div>
        </div>
    );
}

// Arama/hazırlama aşaması göstergesi (dönen ikon + metin + animasyonlu noktalar). İçerik
// akmaya başlayınca MessageBubble bunu göstermeyi keser.
function StatusIndicator({ text }) {
    return (
        <div style={{ display: 'flex', alignItems: 'center', gap: '9px', color: 'rgba(255,255,255,0.62)', fontSize: '14px' }}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" className="animate-spin" style={{ flexShrink: 0 }}>
                <path d="M21 12a9 9 0 1 1-6.219-8.56" />
            </svg>
            <span>{text}<span className="thinking-dots">…</span></span>
        </div>
    );
}

export default function MessageBubble({ msg, copiedId, onCopy, onRetry, onClarificationSelect, onClarificationDismiss, onFollowUpSelect, onFeedbackGiven }) {
    const isUser = msg.role === 'User';
    const images = msg.images || [];
    const [modalSrc, setModalSrc] = useState(null);

    // Feedback state — sadece bu mesaj instance'ı için
    // msg.feedbackGiven: null (verilmemiş) | 1 (like) | -1 (dislike)
    const [feedbackModalOpen, setFeedbackModalOpen] = useState(false);
    const [feedbackError, setFeedbackError] = useState(null);
    const [feedbackSending, setFeedbackSending] = useState(false);

    const sendFeedback = async (rating, payload = {}) => {
        setFeedbackSending(true);
        setFeedbackError(null);
        try {
            await submitFeedback({
                messageId: msg.id,
                rating,
                categories: payload.categories ?? null,
                reasonText: payload.reasonText ?? null,
            });
            onFeedbackGiven?.(msg.id, rating);
            setFeedbackModalOpen(false);
        } catch (err) {
            const apiMsg = err?.response?.data?.error?.message
                || err?.response?.data?.message
                || err?.message
                || 'Geri bildirim gönderilemedi.';
            setFeedbackError(apiMsg);
            // Modal açıksa modal kendi error'ını gösterir — burada throw
            if (feedbackModalOpen) throw err;
        } finally {
            setFeedbackSending(false);
        }
    };

    const handleLike = () => {
        if (msg.feedbackGiven || feedbackSending) return;
        sendFeedback(1).catch(() => { });
    };

    const handleDislikeOpen = () => {
        if (msg.feedbackGiven || feedbackSending) return;
        setFeedbackError(null);
        setFeedbackModalOpen(true);
    };

    if (msg.isClarification) return <ClarificationBubble msg={msg} onClarificationSelect={onClarificationSelect} onClarificationDismiss={onClarificationDismiss} />;

    // Backend standart markdown image syntax üretir: ![alt](/uploads/img_xyz.jpg). ReactMarkdown
    // bunu <img>'ye çevirir; custom 'img' component'i ile ClickableImage olarak render edilir.
    // Kullanıcı URL'yi görmez, sadece görseli.
    const ClickableImage = ({ src, alt = 'görsel', inTable = false }) => {
        // Relative path geldiyse API_BASE prefix ekle (örn. /uploads/img.jpg → http://localhost/uploads/img.jpg)
        const fullSrc = src?.startsWith('http') ? src : `${API_BASE}${src}`;
        return (
            <img
                src={fullSrc}
                alt={alt}
                onClick={() => setModalSrc(fullSrc)}
                onError={(e) => { e.currentTarget.style.display = 'none'; }}
                style={{
                    display: 'block',
                    width: inTable ? '120px' : 'auto',
                    height: inTable ? '120px' : 'auto',
                    maxWidth: inTable ? '120px' : '360px',
                    maxHeight: inTable ? '120px' : '280px',
                    objectFit: 'contain',
                    borderRadius: inTable ? '6px' : '10px',
                    border: '1px solid var(--border)',
                    cursor: 'zoom-in',
                    transition: 'transform 0.15s ease, box-shadow 0.15s ease',
                    margin: inTable ? '0' : '8px 0',
                }}
                onMouseEnter={e => {
                    e.currentTarget.style.transform = 'scale(1.02)';
                    e.currentTarget.style.boxShadow = '0 8px 24px rgba(0,0,0,0.25)';
                }}
                onMouseLeave={e => {
                    e.currentTarget.style.transform = 'scale(1)';
                    e.currentTarget.style.boxShadow = 'none';
                }}
            />
        );
    };

    const components = {
        code({ className, children }) {
            const match = /language-(\w+)/.exec(className || '');
            return match ? (
                <SyntaxHighlighter language={match[1]} style={atomOneDark}
                    customStyle={{ borderRadius: '8px', fontSize: '0.8rem', margin: '8px 0' }}>
                    {String(children).replace(/\n$/, '')}
                </SyntaxHighlighter>
            ) : (
                <code style={{ background: 'var(--surface3, #1e293b)', padding: '2px 6px', borderRadius: '4px', fontSize: '0.85em' }}>
                    {children}
                </code>
            );
        },
        table: ({ node, ...props }) => (
            <div className="table-wrapper"><table {...props} /></div>
        ),
        // Standart markdown image → ClickableImage (modal + hover efekti)
        img: ({ src, alt }) => <ClickableImage src={src} alt={alt} inTable={false} />,
        // Tablo hücresinde sadece img varsa → small inTable görseli
        td: ({ node, children, ...props }) => {
            // Eğer td'nin tek child'ı bir img ise inTable=true ile render et
            const isJustImage = node?.children?.length === 1 && node.children[0]?.tagName === 'img';
            if (isJustImage) {
                const imgNode = node.children[0];
                return (
                    <td {...props}>
                        <ClickableImage src={imgNode.properties?.src} alt={imgNode.properties?.alt} inTable={true} />
                    </td>
                );
            }
            return <td {...props}>{children}</td>;
        },
    };

    return (
        <>
            {modalSrc && <ImageModal src={modalSrc} onClose={() => setModalSrc(null)} />}

            <div style={{ display: 'flex', justifyContent: isUser ? 'flex-end' : 'flex-start', alignItems: 'flex-start', gap: '12px' }}>
                {!isUser && (
                    <div style={{ width: '34px', height: '34px', borderRadius: '10px', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0, marginTop: '4px', background: 'var(--gradient-accent)', boxShadow: '0 6px 18px -6px rgba(var(--accent-rgb),0.6)' }}>
                        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
                            <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                        </svg>
                    </div>
                )}
                <div style={{ maxWidth: '48rem', minWidth: 0, ...(isUser ? {} : { flex: 1 }) }}>
                    <div className="prose-dark" style={{
                        borderRadius: '18px', padding: '13px 18px', fontSize: '14.5px',
                        background: isUser ? 'var(--gradient-user-bubble)' : 'rgba(30, 35, 55, 0.55)',
                        backdropFilter: !isUser ? 'blur(20px) saturate(160%)' : undefined,
                        WebkitBackdropFilter: !isUser ? 'blur(20px) saturate(160%)' : undefined,
                        border: isUser ? '1px solid rgba(255,255,255,0.12)' : '1px solid rgba(255,255,255,0.08)',
                        color: isUser ? 'white' : '#e8eaf0',
                        borderTopRightRadius: isUser ? '6px' : '18px',
                        borderTopLeftRadius: !isUser ? '6px' : '18px',
                        lineHeight: '1.7',
                        boxShadow: isUser
                            ? '0 10px 30px -8px rgba(var(--accent-rgb),0.45), 0 0 0 1px rgba(255,255,255,0.06) inset'
                            : '0 8px 24px -10px rgba(0,0,0,0.4), 0 0 0 1px rgba(255,255,255,0.04) inset',
                    }}>
                        {!isUser ? (
                            <>
                                {msg.content ? (
                                    <ReactMarkdown remarkPlugins={[remarkGfm]} components={components}>
                                        {sanitizeStreamingMarkdown(msg.content, msg.isStreaming)}
                                    </ReactMarkdown>
                                ) : msg.statusText ? (
                                    <StatusIndicator text={msg.statusText} />
                                ) : null}
                                {/* İmleç sadece içerik akarken; arama/hazırlama aşamasında StatusIndicator gösterilir */}
                                {msg.isStreaming && msg.content && (
                                    <span
                                        aria-label="cevap akıyor"
                                        style={{
                                            display: 'inline-block',
                                            width: '8px',
                                            height: '14px',
                                            background: 'currentColor',
                                            opacity: 0.7,
                                            marginLeft: '2px',
                                            verticalAlign: 'baseline',
                                            animation: 'streamingPulse 1s infinite',
                                            borderRadius: '1px',
                                        }}
                                    />
                                )}
                            </>
                        ) : msg.content}
                    </div>

                    {!isUser && msg.badge && !msg.isStreaming && (
                        <div style={{
                            marginTop: '8px',
                            padding: '8px 12px',
                            borderRadius: '10px',
                            background: 'rgba(251, 191, 36, 0.08)',
                            border: '1px solid rgba(251, 191, 36, 0.18)',
                            color: 'rgba(252, 211, 77, 0.95)',
                            fontSize: '12.5px',
                            lineHeight: 1.5,
                        }}>
                            {msg.badge}
                        </div>
                    )}

                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginTop: '6px', justifyContent: isUser ? 'flex-end' : 'flex-start', flexWrap: 'wrap' }}>
                        <span style={{ fontSize: '12px', color: 'rgba(255,255,255,0.55)', fontWeight: 500 }}>{formatTime(msg.createdAt)}</span>
                        <button onClick={() => onCopy(msg.content, msg.id)}
                            style={{ fontSize: '12px', fontWeight: 500, display: 'flex', alignItems: 'center', gap: '5px', padding: '4px 9px', borderRadius: '8px', color: copiedId === msg.id ? '#86efac' : 'rgba(255,255,255,0.7)', background: copiedId === msg.id ? 'rgba(34,197,94,0.12)' : 'rgba(255,255,255,0.05)', border: '1px solid ' + (copiedId === msg.id ? 'rgba(34,197,94,0.25)' : 'rgba(255,255,255,0.08)'), cursor: 'pointer', transition: 'all 0.15s' }}
                            onMouseEnter={(e) => { if (copiedId !== msg.id) { e.currentTarget.style.background = 'rgba(var(--accent-light-rgb),0.14)'; e.currentTarget.style.borderColor = 'rgba(var(--accent-light-rgb),0.3)'; e.currentTarget.style.color = '#fff'; } }}
                            onMouseLeave={(e) => { if (copiedId !== msg.id) { e.currentTarget.style.background = 'rgba(255,255,255,0.05)'; e.currentTarget.style.borderColor = 'rgba(255,255,255,0.08)'; e.currentTarget.style.color = 'rgba(255,255,255,0.7)'; } }}>
                            {copiedId === msg.id ? (
                                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"><polyline points="20 6 9 17 4 12" /></svg>
                            ) : (
                                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                    <rect x="9" y="9" width="13" height="13" rx="2" /><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
                                </svg>
                            )}
                            {copiedId === msg.id ? 'Kopyalandı' : 'Kopyala'}
                        </button>
                        {msg.isError && onRetry && msg.retryQuestion && (
                            <button onClick={() => onRetry(msg.retryQuestion)}
                                style={{ fontSize: '12px', fontWeight: 500, display: 'flex', alignItems: 'center', gap: '5px', color: '#c4b5fd', background: 'rgba(var(--accent-light-rgb),0.12)', border: '1px solid rgba(var(--accent-light-rgb),0.3)', borderRadius: '8px', padding: '4px 9px', cursor: 'pointer' }}>
                                ↺ Tekrar dene
                            </button>
                        )}

                        {/* 👍 / 👎 — sadece bitmiş asistan mesajları için, hata/clarification hariç */}
                        {!isUser && !msg.isStreaming && !msg.isError && msg.id && (
                            <>
                                <button
                                    onClick={handleLike}
                                    disabled={msg.feedbackGiven != null || feedbackSending}
                                    title={msg.feedbackGiven === 1 ? 'Beğenildi' : 'Cevap işime yaradı'}
                                    style={{
                                        fontSize: '12px', fontWeight: 500,
                                        display: 'flex', alignItems: 'center', gap: '5px',
                                        padding: '4px 9px', borderRadius: '8px',
                                        cursor: (msg.feedbackGiven != null || feedbackSending) ? 'default' : 'pointer',
                                        color: msg.feedbackGiven === 1 ? '#86efac' : 'rgba(255,255,255,0.7)',
                                        background: msg.feedbackGiven === 1 ? 'rgba(34,197,94,0.12)' : 'rgba(255,255,255,0.05)',
                                        border: '1px solid ' + (msg.feedbackGiven === 1 ? 'rgba(34,197,94,0.3)' : 'rgba(255,255,255,0.08)'),
                                        opacity: msg.feedbackGiven === -1 ? 0.35 : 1,
                                        transition: 'all 0.15s',
                                    }}
                                    onMouseEnter={e => { if (msg.feedbackGiven == null && !feedbackSending) { e.currentTarget.style.background = 'rgba(34,197,94,0.14)'; e.currentTarget.style.borderColor = 'rgba(34,197,94,0.3)'; e.currentTarget.style.color = '#86efac'; } }}
                                    onMouseLeave={e => { if (msg.feedbackGiven == null && !feedbackSending) { e.currentTarget.style.background = 'rgba(255,255,255,0.05)'; e.currentTarget.style.borderColor = 'rgba(255,255,255,0.08)'; e.currentTarget.style.color = 'rgba(255,255,255,0.7)'; } }}
                                >
                                    {feedbackSending && msg.feedbackGiven == null ? (
                                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="animate-spin"><path d="M21 12a9 9 0 1 1-6.219-8.56" /></svg>
                                    ) : (
                                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M7 10v12" /><path d="M15 5.88 14 10h5.83a2 2 0 0 1 1.92 2.56l-2.33 8A2 2 0 0 1 17.5 22H7V10l4-9c1.66 0 3 1.34 3 3v1.88Z" /></svg>
                                    )}
                                    {feedbackSending && msg.feedbackGiven == null
                                        ? 'Gönderiliyor…'
                                        : (msg.feedbackGiven === 1 ? 'Beğenildi' : 'Beğen')}
                                </button>

                                <button
                                    onClick={handleDislikeOpen}
                                    disabled={msg.feedbackGiven != null || feedbackSending}
                                    title={msg.feedbackGiven === -1 ? 'Beğenilmedi' : 'Cevabı beğenmedim'}
                                    style={{
                                        fontSize: '12px', fontWeight: 500,
                                        display: 'flex', alignItems: 'center', gap: '5px',
                                        padding: '4px 9px', borderRadius: '8px',
                                        cursor: (msg.feedbackGiven != null || feedbackSending) ? 'default' : 'pointer',
                                        color: msg.feedbackGiven === -1 ? '#fca5a5' : 'rgba(255,255,255,0.7)',
                                        background: msg.feedbackGiven === -1 ? 'rgba(239,68,68,0.12)' : 'rgba(255,255,255,0.05)',
                                        border: '1px solid ' + (msg.feedbackGiven === -1 ? 'rgba(239,68,68,0.3)' : 'rgba(255,255,255,0.08)'),
                                        opacity: msg.feedbackGiven === 1 ? 0.35 : 1,
                                        transition: 'all 0.15s',
                                    }}
                                    onMouseEnter={e => { if (msg.feedbackGiven == null && !feedbackSending) { e.currentTarget.style.background = 'rgba(239,68,68,0.14)'; e.currentTarget.style.borderColor = 'rgba(239,68,68,0.3)'; e.currentTarget.style.color = '#fca5a5'; } }}
                                    onMouseLeave={e => { if (msg.feedbackGiven == null && !feedbackSending) { e.currentTarget.style.background = 'rgba(255,255,255,0.05)'; e.currentTarget.style.borderColor = 'rgba(255,255,255,0.08)'; e.currentTarget.style.color = 'rgba(255,255,255,0.7)'; } }}
                                >
                                    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M17 14V2" /><path d="M9 18.12 10 14H4.17a2 2 0 0 1-1.92-2.56l2.33-8A2 2 0 0 1 6.5 2H17v12l-4 9c-1.66 0-3-1.34-3-3v-1.88Z" /></svg>
                                    {msg.feedbackGiven === -1 ? 'Beğenilmedi' : 'Beğenme'}
                                </button>
                            </>
                        )}
                    </div>

                    {feedbackError && !feedbackModalOpen && (
                        <div style={{ marginTop: '6px', fontSize: '11.5px', color: '#fca5a5' }}>
                            {feedbackError}
                        </div>
                    )}

                    <FeedbackModal
                        open={feedbackModalOpen}
                        onSubmit={(payload) => sendFeedback(-1, payload)}
                        onClose={() => setFeedbackModalOpen(false)}
                    />

                    {!isUser && msg.followUpQuestions?.length > 0 && onFollowUpSelect && (
                        <div style={{ marginTop: '10px', display: 'flex', flexDirection: 'column', gap: '6px' }}>
                            <span style={{ fontSize: '12px', color: 'rgba(255,255,255,0.5)', fontWeight: 500 }}>İlgili sorular</span>
                            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '7px' }}>
                                {msg.followUpQuestions.map((q, i) => (
                                    <button key={i} onClick={() => onFollowUpSelect(q, msg.id)}
                                        style={{ textAlign: 'left', padding: '7px 12px', borderRadius: '14px', background: 'rgba(var(--accent-light-rgb),0.08)', border: '1px solid rgba(var(--accent-light-rgb),0.25)', color: '#c4b5fd', cursor: 'pointer', fontSize: '12.5px', transition: 'all 0.15s' }}
                                        onMouseEnter={e => { e.currentTarget.style.background = 'rgba(var(--accent-light-rgb),0.18)'; e.currentTarget.style.borderColor = 'rgba(var(--accent-light-rgb),0.5)'; e.currentTarget.style.color = '#fff'; }}
                                        onMouseLeave={e => { e.currentTarget.style.background = 'rgba(var(--accent-light-rgb),0.08)'; e.currentTarget.style.borderColor = 'rgba(var(--accent-light-rgb),0.25)'; e.currentTarget.style.color = '#c4b5fd'; }}>
                                        {q}
                                    </button>
                                ))}
                            </div>
                        </div>
                    )}
                </div>
            </div>
        </>
    );
}