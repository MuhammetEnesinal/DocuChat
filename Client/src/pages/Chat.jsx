import { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import ReactMarkdown from 'react-markdown';
import { askQuestion, getSessions, getMessages, deleteSession, renameSession } from '../services/api';
import useAuthStore from '../store/authStore';

function formatTime(dateStr) {
    if (!dateStr) return '';
    const d = new Date(dateStr);
    return d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' });
}

function formatDate(dateStr) {
    if (!dateStr) return '';
    const d = new Date(dateStr);
    return d.toLocaleDateString('tr-TR', { day: 'numeric', month: 'long' });
}

export default function Chat() {
    const [sessions, setSessions] = useState([]);
    const [activeSession, setActiveSession] = useState(null);
    const [messages, setMessages] = useState([]);
    const [question, setQuestion] = useState('');
    const [loading, setLoading] = useState(false);
    const [chunks, setChunks] = useState([]);
    const [showChunks, setShowChunks] = useState(false);
    const [copiedId, setCopiedId] = useState(null);
    const [editingSessionId, setEditingSessionId] = useState(null);
    const [editingTitle, setEditingTitle] = useState('');
    const messagesEndRef = useRef(null);
    const renameInputRef = useRef(null);
    const { user, logout } = useAuthStore();
    const navigate = useNavigate();

    useEffect(() => { fetchSessions(); }, []);
    useEffect(() => { messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' }); }, [messages]);
    useEffect(() => {
        if (editingSessionId && renameInputRef.current) renameInputRef.current.focus();
    }, [editingSessionId]);

    const fetchSessions = async () => {
        try {
            const res = await getSessions();
            setSessions(res.data.data || []);
        } catch { }
    };

    const loadSession = async (session) => {
        setActiveSession(session);
        setChunks([]);
        setShowChunks(false);
        try {
            const res = await getMessages(session.id);
            setMessages(res.data.data || []);
        } catch { }
    };

    const newChat = () => {
        setActiveSession(null);
        setMessages([]);
        setChunks([]);
        setShowChunks(false);
    };

    const handleSend = async () => {
        if (!question.trim() || loading) return;
        const q = question.trim();
        setQuestion('');
        setLoading(true);

        const userMsg = { role: 'User', content: q, id: Date.now(), createdAt: new Date().toISOString() };
        setMessages((prev) => [...prev, userMsg]);

        try {
            const res = await askQuestion(q, activeSession?.id || null);
            const { sessionId, answer, sourceChunks } = res.data.data;

            if (!activeSession) {
                const newSession = { id: sessionId, title: q.slice(0, 60), createdAt: new Date().toISOString() };
                setActiveSession(newSession);
                setSessions((prev) => [newSession, ...prev]);
            }

            setMessages((prev) => [...prev, {
                role: 'Assistant', content: answer,
                id: Date.now() + 1, createdAt: new Date().toISOString()
            }]);
            setChunks(sourceChunks || []);
        } catch {
            setMessages((prev) => [...prev, {
                role: 'Assistant', content: 'Bir hata oluştu.',
                id: Date.now() + 1, createdAt: new Date().toISOString()
            }]);
        } finally {
            setLoading(false);
        }
    };

    const handleCopy = (content, id) => {
        navigator.clipboard.writeText(content);
        setCopiedId(id);
        setTimeout(() => setCopiedId(null), 2000);
    };

    const handleDelete = async (sessionId, e) => {
        e.stopPropagation();
        await deleteSession(sessionId);
        setSessions((prev) => prev.filter((s) => s.id !== sessionId));
        if (activeSession?.id === sessionId) newChat();
    };

    const startRename = (session, e) => {
        e.stopPropagation();
        setEditingSessionId(session.id);
        setEditingTitle(session.title);
    };

    const commitRename = async (sessionId) => {
        const title = editingTitle.trim();
        if (!title) { setEditingSessionId(null); return; }
        try {
            await renameSession(sessionId, title);
            setSessions((prev) => prev.map((s) => s.id === sessionId ? { ...s, title } : s));
            if (activeSession?.id === sessionId) setActiveSession((s) => ({ ...s, title }));
        } catch { }
        setEditingSessionId(null);
    };

    const handleLogout = () => { logout(); navigate('/login'); };

    // Mesajları tarihe göre grupla
    const groupedMessages = messages.reduce((acc, msg) => {
        const date = formatDate(msg.createdAt);
        if (!acc[date]) acc[date] = [];
        acc[date].push(msg);
        return acc;
    }, {});

    return (
        <div className="flex h-screen" style={{ background: 'var(--navy)' }}>
            {/* Sol Panel */}
            <div className="flex flex-col w-64 flex-shrink-0" style={{ background: 'var(--surface)', borderRight: '1px solid var(--border)' }}>
                {/* Logo */}
                <div className="flex items-center gap-3 px-5 py-5" style={{ borderBottom: '1px solid var(--border)' }}>
                    <div className="w-8 h-8 rounded-lg flex items-center justify-center flex-shrink-0" style={{ background: 'var(--accent)' }}>
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
                            <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                        </svg>
                    </div>
                    <span className="font-bold text-white text-base">DocuChat</span>
                </div>

                {/* Yeni Sohbet */}
                <div className="px-3 pt-3">
                    <button onClick={newChat} className="w-full flex items-center gap-2 px-3 py-2.5 rounded-xl text-sm font-medium transition-all" style={{ background: 'var(--accent)', color: 'white' }}>
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                            <line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" />
                        </svg>
                        Yeni Sohbet
                    </button>
                </div>

                {/* Session Listesi */}
                <div className="flex-1 overflow-y-auto px-3 py-3 space-y-1">
                    {sessions.length === 0 && (
                        <p className="text-xs text-center mt-4" style={{ color: 'var(--gray-light)' }}>Henüz sohbet yok</p>
                    )}
                    {sessions.map((s) => (
                        <div key={s.id} onClick={() => loadSession(s)}
                            className="group flex items-center justify-between px-3 py-2.5 rounded-xl cursor-pointer transition-all"
                            style={{
                                background: activeSession?.id === s.id ? 'var(--navy-light)' : 'transparent',
                                border: activeSession?.id === s.id ? '1px solid var(--border)' : '1px solid transparent',
                            }}>
                            {editingSessionId === s.id ? (
                                <input
                                    ref={renameInputRef}
                                    value={editingTitle}
                                    onChange={(e) => setEditingTitle(e.target.value)}
                                    onBlur={() => commitRename(s.id)}
                                    onKeyDown={(e) => {
                                        if (e.key === 'Enter') commitRename(s.id);
                                        if (e.key === 'Escape') setEditingSessionId(null);
                                    }}
                                    onClick={(e) => e.stopPropagation()}
                                    className="flex-1 text-sm bg-transparent outline-none border-b text-white"
                                    style={{ borderColor: 'var(--accent)' }}
                                />
                            ) : (
                                <span className="text-sm truncate flex-1" style={{ color: activeSession?.id === s.id ? '#e2e8f0' : '#94a3b8', maxWidth: '140px' }}>
                                    {s.title || 'Sohbet'}
                                </span>
                            )}
                            <div className="opacity-0 group-hover:opacity-100 flex items-center gap-1 flex-shrink-0 ml-1">
                                {/* Rename */}
                                <button onClick={(e) => startRename(s, e)} className="p-1 rounded transition-all" style={{ color: '#64748b' }}
                                    onMouseEnter={(e) => e.currentTarget.style.color = '#93c5fd'}
                                    onMouseLeave={(e) => e.currentTarget.style.color = '#64748b'}>
                                    <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                        <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
                                        <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
                                    </svg>
                                </button>
                                {/* Delete */}
                                <button onClick={(e) => handleDelete(s.id, e)} className="p-1 rounded transition-all" style={{ color: '#64748b' }}
                                    onMouseEnter={(e) => e.currentTarget.style.color = '#ef4444'}
                                    onMouseLeave={(e) => e.currentTarget.style.color = '#64748b'}>
                                    <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                        <polyline points="3 6 5 6 21 6" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
                                    </svg>
                                </button>
                            </div>
                        </div>
                    ))}
                </div>

                {/* Alt butonlar */}
                <div className="px-3 pb-3 space-y-1" style={{ borderTop: '1px solid var(--border)', paddingTop: '12px' }}>
                    {user?.roles?.includes('Admin') && (
                        <button onClick={() => navigate('/admin')}
                            className="w-full flex items-center gap-2 px-3 py-2.5 rounded-xl text-sm transition-all"
                            style={{ color: '#94a3b8', background: 'transparent' }}
                            onMouseEnter={(e) => e.currentTarget.style.background = 'var(--surface2)'}
                            onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}>
                            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                <rect x="3" y="3" width="7" height="7" /><rect x="14" y="3" width="7" height="7" />
                                <rect x="14" y="14" width="7" height="7" /><rect x="3" y="14" width="7" height="7" />
                            </svg>
                            Admin Panel
                        </button>
                    )}
                    <button onClick={handleLogout}
                        className="w-full flex items-center gap-2 px-3 py-2.5 rounded-xl text-sm transition-all"
                        style={{ color: '#94a3b8' }}
                        onMouseEnter={(e) => e.currentTarget.style.background = 'var(--surface2)'}
                        onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}>
                        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                            <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
                            <polyline points="16 17 21 12 16 7" /><line x1="21" y1="12" x2="9" y2="12" />
                        </svg>
                        Çıkış Yap
                    </button>
                </div>
            </div>

            {/* Ana Alan */}
            <div className="flex flex-col flex-1 min-w-0">
                {/* Header */}
                <div className="flex items-center justify-between px-6 py-4" style={{ borderBottom: '1px solid var(--border)', background: 'var(--surface)' }}>
                    <div>
                        <h1 className="text-base font-semibold text-white">{activeSession?.title || 'Yeni Sohbet'}</h1>
                        <p className="text-xs mt-0.5" style={{ color: 'var(--gray-light)' }}>Tüm belgeler üzerinde arama yapılıyor</p>
                    </div>
                    {chunks.length > 0 && (
                        <button onClick={() => setShowChunks(!showChunks)}
                            className="flex items-center gap-2 px-3 py-1.5 rounded-lg text-xs font-medium transition-all"
                            style={{ background: showChunks ? 'var(--accent)' : 'var(--surface2)', color: showChunks ? 'white' : '#94a3b8', border: '1px solid var(--border)' }}>
                            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                                <polyline points="14 2 14 8 20 8" />
                            </svg>
                            Kaynaklar ({chunks.length})
                        </button>
                    )}
                </div>

                <div className="flex flex-1 min-h-0">
                    {/* Mesajlar */}
                    <div className="flex flex-col flex-1 min-w-0">
                        <div className="flex-1 overflow-y-auto px-6 py-6 space-y-6">
                            {messages.length === 0 && (
                                <div className="flex flex-col items-center justify-center h-full text-center py-20">
                                    <div className="w-16 h-16 rounded-2xl flex items-center justify-center mb-5"
                                        style={{ background: 'var(--surface2)', border: '1px solid var(--border)' }}>
                                        <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="#3b82f6" strokeWidth="1.5">
                                            <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                                        </svg>
                                    </div>
                                    <h2 className="text-lg font-semibold text-white mb-2">Belgeleri sorgulayın</h2>
                                    <p className="text-sm max-w-sm mb-6" style={{ color: 'var(--gray-light)' }}>
                                        Yüklü belgeler hakkında sorularınızı sorun.
                                    </p>
                                    <div className="grid grid-cols-2 gap-2 w-full max-w-md">
                                        {['Bu belgeler ne hakkında?', 'Özet çıkar', 'Önemli maddeleri listele', 'Daha fazla anlat'].map((hint) => (
                                            <button key={hint} onClick={() => setQuestion(hint)}
                                                className="px-3 py-2.5 rounded-xl text-xs text-left transition-all"
                                                style={{ background: 'var(--surface2)', border: '1px solid var(--border)', color: '#94a3b8' }}
                                                onMouseEnter={(e) => { e.currentTarget.style.borderColor = 'var(--accent)'; e.currentTarget.style.color = '#e2e8f0'; }}
                                                onMouseLeave={(e) => { e.currentTarget.style.borderColor = 'var(--border)'; e.currentTarget.style.color = '#94a3b8'; }}>
                                                {hint}
                                            </button>
                                        ))}
                                    </div>
                                </div>
                            )}

                            {Object.entries(groupedMessages).map(([date, msgs]) => (
                                <div key={date}>
                                    {/* Tarih ayracı */}
                                    <div className="flex items-center gap-3 my-4">
                                        <div className="flex-1 h-px" style={{ background: 'var(--border)' }} />
                                        <span className="text-xs px-2" style={{ color: 'var(--gray-light)' }}>{date}</span>
                                        <div className="flex-1 h-px" style={{ background: 'var(--border)' }} />
                                    </div>

                                    <div className="space-y-6">
                                        {msgs.map((msg) => (
                                            <div key={msg.id} className={`flex ${msg.role === 'User' ? 'justify-end' : 'justify-start'}`}>
                                                {msg.role === 'Assistant' && (
                                                    <div className="w-8 h-8 rounded-lg flex items-center justify-center flex-shrink-0 mr-3 mt-1"
                                                        style={{ background: 'var(--accent)' }}>
                                                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
                                                            <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                                                        </svg>
                                                    </div>
                                                )}
                                                <div className="max-w-2xl">
                                                    <div className="rounded-2xl px-4 py-3 text-sm prose-dark"
                                                        style={{
                                                            background: msg.role === 'User' ? 'var(--accent)' : 'var(--surface2)',
                                                            border: msg.role === 'User' ? 'none' : '1px solid var(--border)',
                                                            color: msg.role === 'User' ? 'white' : '#e2e8f0',
                                                            borderTopRightRadius: msg.role === 'User' ? '4px' : '16px',
                                                            borderTopLeftRadius: msg.role === 'Assistant' ? '4px' : '16px',
                                                            lineHeight: '1.7',
                                                        }}>
                                                        {msg.role === 'Assistant'
                                                            ? <ReactMarkdown>{msg.content}</ReactMarkdown>
                                                            : msg.content}
                                                    </div>
                                                    {/* Alt satır: saat + kopyala */}
                                                    <div className={`flex items-center gap-2 mt-1 ${msg.role === 'User' ? 'justify-end' : 'justify-start'}`}>
                                                        <span className="text-xs" style={{ color: '#475569' }}>{formatTime(msg.createdAt)}</span>
                                                        {msg.role === 'Assistant' && (
                                                            <button onClick={() => handleCopy(msg.content, msg.id)}
                                                                className="text-xs flex items-center gap-1 transition-all"
                                                                style={{ color: copiedId === msg.id ? '#4ade80' : '#475569' }}>
                                                                {copiedId === msg.id ? (
                                                                    <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                                                                        <polyline points="20 6 9 17 4 12" />
                                                                    </svg>
                                                                ) : (
                                                                    <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                                                        <rect x="9" y="9" width="13" height="13" rx="2" /><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
                                                                    </svg>
                                                                )}
                                                                {copiedId === msg.id ? 'Kopyalandı' : 'Kopyala'}
                                                            </button>
                                                        )}
                                                    </div>
                                                </div>
                                            </div>
                                        ))}
                                    </div>
                                </div>
                            ))}

                            {loading && (
                                <div className="flex justify-start">
                                    <div className="w-8 h-8 rounded-lg flex items-center justify-center flex-shrink-0 mr-3" style={{ background: 'var(--accent)' }}>
                                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
                                            <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                                        </svg>
                                    </div>
                                    <div className="px-4 py-3 rounded-2xl rounded-tl-sm text-sm" style={{ background: 'var(--surface2)', border: '1px solid var(--border)' }}>
                                        <div className="flex gap-1 items-center">
                                            <div className="w-2 h-2 rounded-full animate-bounce" style={{ background: 'var(--accent)', animationDelay: '0ms' }} />
                                            <div className="w-2 h-2 rounded-full animate-bounce" style={{ background: 'var(--accent)', animationDelay: '150ms' }} />
                                            <div className="w-2 h-2 rounded-full animate-bounce" style={{ background: 'var(--accent)', animationDelay: '300ms' }} />
                                        </div>
                                    </div>
                                </div>
                            )}
                            <div ref={messagesEndRef} />
                        </div>

                        {/* Input */}
                        <div className="px-6 pb-6">
                            <div className="flex gap-3 items-end p-3 rounded-2xl" style={{ background: 'var(--surface2)', border: '1px solid var(--border)' }}>
                                <textarea
                                    value={question}
                                    onChange={(e) => setQuestion(e.target.value)}
                                    onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); handleSend(); } }}
                                    placeholder="Belge hakkında soru sorun... (Enter ile gönder)"
                                    rows={1}
                                    style={{ resize: 'none', minHeight: '44px', maxHeight: '160px', overflowY: 'auto', background: 'transparent', border: 'none', outline: 'none', color: '#e2e8f0', fontSize: '0.9rem', flex: 1, padding: '8px 4px', lineHeight: '1.6' }}
                                    onInput={(e) => { e.target.style.height = 'auto'; e.target.style.height = e.target.scrollHeight + 'px'; }}
                                />
                                <button onClick={handleSend} disabled={loading || !question.trim()}
                                    className="w-10 h-10 rounded-xl flex items-center justify-center flex-shrink-0 transition-all"
                                    style={{ background: loading || !question.trim() ? 'var(--navy-light)' : 'var(--accent)', cursor: loading || !question.trim() ? 'not-allowed' : 'pointer' }}>
                                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
                                        <line x1="22" y1="2" x2="11" y2="13" /><polygon points="22 2 15 22 11 13 2 9 22 2" />
                                    </svg>
                                </button>
                            </div>
                            <p className="text-center text-xs mt-2" style={{ color: 'var(--gray-light)' }}>
                                Enter ile gönder · Shift+Enter yeni satır
                            </p>
                        </div>
                    </div>

                    {/* Kaynak Chunks Panel */}
                    {showChunks && chunks.length > 0 && (
                        <div className="w-80 flex-shrink-0 overflow-y-auto" style={{ background: 'var(--surface)', borderLeft: '1px solid var(--border)' }}>
                            <div className="px-4 py-4" style={{ borderBottom: '1px solid var(--border)' }}>
                                <h3 className="font-semibold text-white text-sm">Kaynak Belgeler</h3>
                                <p className="text-xs mt-1" style={{ color: 'var(--gray-light)' }}>{chunks.length} ilgili bölüm bulundu</p>
                            </div>
                            <div className="p-3 space-y-3">
                                {chunks.map((chunk, i) => (
                                    <div key={i} className="rounded-xl p-3" style={{ background: 'var(--surface2)', border: '1px solid var(--border)' }}>
                                        <div className="flex items-center gap-2 mb-2">
                                            <span className="text-xs px-2 py-0.5 rounded-md font-medium"
                                                style={{ background: 'rgba(59,130,246,0.15)', color: '#93c5fd', border: '1px solid rgba(59,130,246,0.2)' }}>
                                                #{i + 1}
                                            </span>
                                            <span className="text-xs truncate" style={{ color: '#94a3b8' }}>{chunk.fileName}</span>
                                        </div>
                                        <p className="text-xs leading-relaxed" style={{ color: '#64748b', display: '-webkit-box', WebkitLineClamp: 4, WebkitBoxOrient: 'vertical', overflow: 'hidden' }}>
                                            {chunk.content}
                                        </p>
                                    </div>
                                ))}
                            </div>
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
}