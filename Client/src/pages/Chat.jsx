import { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { askQuestion, getSessions, getMessages, deleteSession, renameSession, getPopularQuestions } from '../services/api';
import { formatDateShort, getRateLimitMessage } from '../utils/format';
import { useToast } from '../components/shared/Toast';
import { useAuth } from '../hooks/useAuth';
import ChatSidebar from '../components/chat/ChatSidebar';
import MessageBubble from '../components/chat/MessageBubble';
import SourcePanel from '../components/chat/SourcePanel';
import EmptyState from '../components/chat/EmptyState';

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
    const [sessionsLoading, setSessionsLoading] = useState(true);
    const [popularQuestions, setPopularQuestions] = useState([]);
    const [sidebarCollapsed, setSidebarCollapsed] = useState(false);

    const messagesEndRef = useRef(null);
    const textareaRef = useRef(null);
    const { user, logout } = useAuth();
    const navigate = useNavigate();
    const toast = useToast();

    useEffect(() => { fetchSessions(); fetchPopularQuestions(); }, []);
    useEffect(() => { messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' }); }, [messages]);

    const fetchPopularQuestions = async () => {
        try {
            const res = await getPopularQuestions(6);
            setPopularQuestions(res.data.data || []);
        } catch { }
    };

    const fetchSessions = async () => {
        setSessionsLoading(true);
        try {
            const res = await getSessions();
            setSessions(res.data.data || []);
        } catch { toast.error('Sohbet listesi yüklenemedi.'); }
        finally { setSessionsLoading(false); }
    };

    const loadSession = async (session) => {
        setActiveSession(session);
        setChunks([]);
        setShowChunks(false);
        try {
            const res = await getMessages(session.id);
            setMessages(res.data.data || []);
        } catch { toast.error('Mesajlar yüklenemedi.'); }
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
        if (textareaRef.current) textareaRef.current.style.height = 'auto';
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
                id: Date.now() + 1, createdAt: new Date().toISOString(),
            }]);
            setChunks(sourceChunks || []);
        } catch (err) {
            const msg = getRateLimitMessage(err);
            toast.error(msg);
            setMessages((prev) => [...prev, {
                role: 'Assistant',
                content: err?.response?.status === 429
                    ? '⏳ Sunucu meşgul. Lütfen birkaç saniye bekleyip tekrar deneyin.'
                    : 'Bir hata oluştu. Lütfen tekrar deneyin.',
                id: Date.now() + 1, createdAt: new Date().toISOString(),
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

    const handleDeleteSession = async (sessionId) => {
        try {
            await deleteSession(sessionId);
            setSessions((prev) => prev.filter((s) => s.id !== sessionId));
            if (activeSession?.id === sessionId) newChat();
            toast.success('Sohbet silindi.');
        } catch { toast.error('Sohbet silinemedi.'); }
    };

    const handleStartRename = (session) => {
        setEditingSessionId(session.id);
        setEditingTitle(session.title);
    };

    const handleCommitRename = async (sessionId) => {
        const title = editingTitle.trim();
        if (!title) { setEditingSessionId(null); return; }
        try {
            await renameSession(sessionId, title);
            setSessions((prev) => prev.map((s) => s.id === sessionId ? { ...s, title } : s));
            if (activeSession?.id === sessionId) setActiveSession((s) => ({ ...s, title }));
            toast.success('Sohbet adı güncellendi.');
        } catch { toast.error('Sohbet adı güncellenemedi.'); }
        setEditingSessionId(null);
    };

    const handleLogout = () => { logout(); navigate('/login'); };

    const groupedMessages = messages.reduce((acc, msg) => {
        const date = formatDateShort(msg.createdAt);
        if (!acc[date]) acc[date] = [];
        acc[date].push(msg);
        return acc;
    }, {});

    return (
        <div className="flex h-screen" style={{ background: 'var(--navy)' }}>
            {/* Sidebar */}
            <ChatSidebar
                sessions={sessions}
                sessionsLoading={sessionsLoading}
                activeSession={activeSession}
                editingSessionId={editingSessionId}
                editingTitle={editingTitle}
                collapsed={sidebarCollapsed}
                onToggleCollapse={() => setSidebarCollapsed(!sidebarCollapsed)}
                onNewChat={newChat}
                onLoadSession={loadSession}
                onStartRename={handleStartRename}
                onCommitRename={handleCommitRename}
                onSetEditingTitle={setEditingTitle}
                onSetEditingSessionId={setEditingSessionId}
                onDeleteSession={handleDeleteSession}
                user={user}
                onLogout={handleLogout}
            />

            {/* Ana Alan */}
            <div className="flex flex-col flex-1 min-w-0">
                {/* Header */}
                <div className="flex items-center justify-between px-4 sm:px-6 py-4"
                    style={{ borderBottom: '1px solid var(--border)', background: 'var(--surface)' }}>
                    <div className="min-w-0">
                        <h1 className="text-base font-semibold text-white truncate">
                            {activeSession?.title || 'Yeni Sohbet'}
                        </h1>
                        <p className="text-xs mt-0.5" style={{ color: 'var(--gray-light)' }}>
                            Tüm belgeler üzerinde arama yapılıyor
                        </p>
                    </div>
                    {chunks.length > 0 && (
                        <button onClick={() => setShowChunks(!showChunks)}
                            className="flex items-center gap-2 px-3 py-1.5 rounded-lg text-xs font-medium transition-all flex-shrink-0 ml-3"
                            style={{
                                background: showChunks ? 'var(--accent)' : 'var(--surface2)',
                                color: showChunks ? 'white' : '#94a3b8',
                                border: '1px solid var(--border)',
                            }}>
                            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                                <polyline points="14 2 14 8 20 8" />
                            </svg>
                            <span className="hidden sm:inline">Kaynaklar</span> ({chunks.length})
                        </button>
                    )}
                </div>

                <div className="flex flex-1 min-h-0">
                    {/* Mesaj alanı */}
                    <div className="flex flex-col flex-1 min-w-0">
                        <div className="flex-1 overflow-y-auto px-4 sm:px-6 py-6 space-y-6">
                            {messages.length === 0 && (
                                <EmptyState
                                    popularQuestions={popularQuestions}
                                    onSelectQuestion={setQuestion}
                                />
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
                                            <MessageBubble
                                                key={msg.id}
                                                msg={msg}
                                                copiedId={copiedId}
                                                onCopy={handleCopy}
                                            />
                                        ))}
                                    </div>
                                </div>
                            ))}

                            {/* Typing indicator */}
                            {loading && (
                                <div className="flex justify-start">
                                    <div className="w-8 h-8 rounded-lg flex items-center justify-center flex-shrink-0 mr-3"
                                        style={{ background: 'var(--accent)' }}>
                                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
                                            <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
                                        </svg>
                                    </div>
                                    <div className="px-4 py-3 rounded-2xl rounded-tl-sm text-sm"
                                        style={{ background: 'var(--surface2)', border: '1px solid var(--border)' }}>
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
                        <div className="px-4 sm:px-6 pb-4 sm:pb-6">
                            <div className="flex gap-3 items-end p-3 rounded-2xl"
                                style={{ background: 'var(--surface2)', border: '1px solid var(--border)' }}>
                                <textarea
                                    ref={textareaRef}
                                    value={question}
                                    onChange={(e) => setQuestion(e.target.value)}
                                    onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); handleSend(); } }}
                                    placeholder="Belge hakkında soru sorun... (Enter ile gönder)"
                                    rows={1}
                                    style={{
                                        resize: 'none', minHeight: '44px', maxHeight: '160px',
                                        overflowY: 'auto', background: 'transparent', border: 'none',
                                        outline: 'none', color: '#e2e8f0', fontSize: '0.9rem',
                                        flex: 1, padding: '8px 4px', lineHeight: '1.6',
                                    }}
                                    onInput={(e) => { e.target.style.height = 'auto'; e.target.style.height = e.target.scrollHeight + 'px'; }}
                                />
                                <button onClick={handleSend} disabled={loading || !question.trim()}
                                    className="w-10 h-10 rounded-xl flex items-center justify-center flex-shrink-0 transition-all"
                                    style={{
                                        background: loading || !question.trim() ? 'var(--navy-light)' : 'var(--accent)',
                                        cursor: loading || !question.trim() ? 'not-allowed' : 'pointer',
                                    }}>
                                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
                                        <line x1="22" y1="2" x2="11" y2="13" />
                                        <polygon points="22 2 15 22 11 13 2 9 22 2" />
                                    </svg>
                                </button>
                            </div>
                            <p className="text-center text-xs mt-2 hidden sm:block" style={{ color: 'var(--gray-light)' }}>
                                Enter ile gönder · Shift+Enter yeni satır
                            </p>
                        </div>
                    </div>

                    {/* Kaynak paneli */}
                    {showChunks && chunks.length > 0 && (
                        <SourcePanel chunks={chunks} />
                    )}
                </div>
            </div>
        </div>
    );
}