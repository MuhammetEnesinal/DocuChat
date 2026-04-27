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
import ChatInput from '../components/chat/ChatInput';
import TypingIndicator from '../components/chat/TypingIndicator';

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
    const [sidebarCollapsed, setSidebarCollapsed] = useState(window.innerWidth < 768);

    const messagesEndRef = useRef(null);
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
        if (window.innerWidth < 768) setSidebarCollapsed(true);
        try {
            const res = await getMessages(session.id);
            const msgs = (res.data.data || []).map(m => ({
                ...m,
                images: m.imagesJson ? (() => { try { return JSON.parse(m.imagesJson); } catch { return []; } })() : undefined
            }));
            setMessages(msgs);
        } catch { toast.error('Mesajlar yüklenemedi.'); }
    };

    const newChat = () => {
        setActiveSession(null);
        setMessages([]);
        setChunks([]);
        setShowChunks(false);
        if (window.innerWidth < 768) setSidebarCollapsed(true);
    };

    const handleSend = async () => {
        if (!question.trim() || loading) return;
        const q = question.trim();
        setQuestion('');
        setLoading(true);

        setMessages((prev) => [...prev, { role: 'User', content: q, id: Date.now(), createdAt: new Date().toISOString() }]);

        try {
            const res = await askQuestion(q, activeSession?.id || null);
            const { sessionId, answer, sourceChunks } = res.data.data;

            if (!activeSession) {
                const newSession = { id: sessionId, title: q.slice(0, 60), createdAt: new Date().toISOString() };
                setActiveSession(newSession);
                setSessions((prev) => [newSession, ...prev]);
            }

            // Chunk'lardan imagePath olanları topla — JSON array olabilir
            const images = (sourceChunks || [])
                .flatMap(c => {
                    if (!c.imagePath) return [];
                    try {
                        const parsed = JSON.parse(c.imagePath);
                        return Array.isArray(parsed) ? parsed : [c.imagePath];
                    } catch {
                        return [c.imagePath];
                    }
                })
                .filter(Boolean);

            setMessages((prev) => [...prev, {
                role: 'Assistant',
                content: answer,
                id: Date.now() + 1,
                createdAt: new Date().toISOString(),
                images: images.length > 0 ? images : undefined
            }]);
            setChunks(sourceChunks || []);
        } catch (err) {
            toast.error(getRateLimitMessage(err));
            setMessages((prev) => [...prev, {
                role: 'Assistant',
                content: err?.response?.status === 429 ? '⏳ Sunucu meşgul. Lütfen birkaç saniye bekleyip tekrar deneyin.' : 'Bir hata oluştu. Lütfen tekrar deneyin.',
                id: Date.now() + 1, createdAt: new Date().toISOString(),
            }]);
        } finally { setLoading(false); }
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

    const handleStartRename = (session) => { setEditingSessionId(session.id); setEditingTitle(session.title); };

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

    const groupedMessages = messages.reduce((acc, msg) => {
        const date = formatDateShort(msg.createdAt);
        if (!acc[date]) acc[date] = [];
        acc[date].push(msg);
        return acc;
    }, {});

    return (
        <div style={{ display: 'flex', height: '100vh', background: 'var(--navy)' }}>
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
                onLogout={() => { logout(); navigate('/login'); }}
            />

            <div style={{ display: 'flex', flexDirection: 'column', flex: 1, minWidth: 0 }}>
                {/* Header */}
                <div className="chat-header" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '16px 24px', borderBottom: '1px solid var(--border)', background: 'var(--surface)' }}>
                    <div style={{ minWidth: 0 }}>
                        <h1 style={{ fontSize: '15px', fontWeight: 600, color: 'white', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', margin: 0 }}>
                            {activeSession?.title || 'Yeni Sohbet'}
                        </h1>
                        <p style={{ fontSize: '12px', marginTop: '2px', color: 'var(--gray-light)', margin: '2px 0 0' }}>
                            Tüm belgeler üzerinde arama yapılıyor
                        </p>
                    </div>
                    {chunks.length > 0 && (
                        <button onClick={() => setShowChunks(!showChunks)}
                            style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '6px 12px', borderRadius: '8px', fontSize: '13px', fontWeight: 500, flexShrink: 0, marginLeft: '12px', cursor: 'pointer', background: showChunks ? 'var(--accent)' : 'var(--surface2)', color: showChunks ? 'white' : '#94a3b8', border: '1px solid var(--border)' }}>
                            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" />
                            </svg>
                            Kaynaklar ({chunks.length})
                        </button>
                    )}
                </div>

                <div style={{ display: 'flex', flex: 1, minHeight: 0 }}>
                    {/* Mesaj alanı */}
                    <div style={{ display: 'flex', flexDirection: 'column', flex: 1, minWidth: 0 }}>
                        <div style={{ flex: 1, overflowY: 'auto', padding: '24px', display: 'flex', flexDirection: 'column', gap: '24px' }}>
                            {messages.length === 0 && (
                                <EmptyState popularQuestions={popularQuestions} onSelectQuestion={setQuestion} />
                            )}

                            {Object.entries(groupedMessages).map(([date, msgs]) => (
                                <div key={date}>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px', margin: '8px 0' }}>
                                        <div style={{ flex: 1, height: '1px', background: 'var(--border)' }} />
                                        <span style={{ fontSize: '12px', color: 'var(--gray-light)', padding: '0 8px' }}>{date}</span>
                                        <div style={{ flex: 1, height: '1px', background: 'var(--border)' }} />
                                    </div>
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                                        {msgs.map((msg) => (
                                            <MessageBubble key={msg.id} msg={msg} copiedId={copiedId} onCopy={handleCopy} />
                                        ))}
                                    </div>
                                </div>
                            ))}

                            {loading && <TypingIndicator />}
                            <div ref={messagesEndRef} />
                        </div>

                        <ChatInput
                            value={question}
                            onChange={setQuestion}
                            onSend={handleSend}
                            loading={loading}
                        />
                    </div>

                    {showChunks && chunks.length > 0 && <SourcePanel chunks={chunks} />}
                </div>
            </div>
        </div>
    );
}