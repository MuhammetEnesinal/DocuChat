const CHANNEL_NAME = 'docuchat-sessions';

let _channel = null;

function getChannel() {
    if (typeof window === 'undefined' || !('BroadcastChannel' in window)) return null;
    if (!_channel) _channel = new BroadcastChannel(CHANNEL_NAME);
    return _channel;
}

export function broadcastSessionDeleted(id) {
    getChannel()?.postMessage({ type: 'session-deleted', id });
}

export function broadcastSessionRenamed(id, title) {
    getChannel()?.postMessage({ type: 'session-renamed', id, title });
}

export function broadcastSessionCreated(session) {
    if (!session?.id) return;
    getChannel()?.postMessage({ type: 'session-created', id: session.id, session });
}

export function subscribeSessions(handler) {
    const ch = getChannel();
    if (!ch) return () => {};
    ch.addEventListener('message', handler);
    return () => ch.removeEventListener('message', handler);
}
