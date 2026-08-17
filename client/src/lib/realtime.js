import * as signalR from '@microsoft/signalr';
import { API_BASE } from '../services/api';

// Tek gerçek zamanlı bağlantı (SignalR). Desen: "sinyal taşı, veri taşıma" —
// sunucu { type, payload } event'i yollar, hook'lar bunu alıp mevcut REST fetch'ini tazeler.
// sessionsChannel (BroadcastChannel) aynı tarayıcı sekmelerini senkronlar; bu modül ise
// kullanıcılar/cihazlar/backend arası senkronu ekler. İkisi birlikte tam canlı sistemi verir.

let connection = null;
let manualStop = false;               // stopRealtime çağrıldı mı — istemsiz kapanıştan ayırt et
const eventHandlers = new Set();      // (evt) => void      — her "event" sinyalinde
const reconnectHandlers = new Set();  // () => void         — yeniden bağlanınca (kaçan sinyalleri telafi)

function buildConnection() {
    return new signalR.HubConnectionBuilder()
        .withUrl(`${API_BASE}/hubs/notifications`, {
            // WebSocket handshake Authorization header taşıyamaz → token query'e (backend /hubs'ta okur).
            accessTokenFactory: () => localStorage.getItem('token') || '',
        })
        // Sonsuz yeniden bağlanma: kısa aralıklarla başlar, 30 sn'de sabitlenir. null döndürmediğimiz
        // için SignalR asla pes etmez (geçici ağ/uyku sonrası kendini toparlar).
        .withAutomaticReconnect({
            nextRetryDelayInMilliseconds: (ctx) =>
                Math.min(30000, 1000 * 2 ** Math.min(ctx.previousRetryCount, 5)),
        })
        .configureLogging(signalR.LogLevel.Warning)
        .build();
}

function dispatch(evt) {
    eventHandlers.forEach((h) => { try { h(evt); } catch (e) { console.error('realtime handler', e); } });
}

// Kaçan sinyalleri telafi için "her şeyi tazele" — reconnect'te (otomatik) ve token yenileme
// sonrası manuel reconnect'te (scope değişti) çağrılır.
export function runReconnectHandlers() {
    reconnectHandlers.forEach((h) => { try { h(); } catch (e) { console.error('realtime reconnect', e); } });
}

// Bağlantıyı başlatır (idempotent — zaten varsa no-op). Login sonrası çağrılır.
export async function startRealtime() {
    if (connection) return connection;
    manualStop = false;
    connection = buildConnection();

    connection.on('event', dispatch);

    connection.onreconnected(runReconnectHandlers);

    connection.onclose(() => {
        // Otomatik reconnect politikası pes etmez; buraya normalde yalnız manuel stop düşer.
        // Yine de token hâlâ varsa ve istemsiz kapandıysa, güvenlik ağı olarak yeniden dene.
        if (!manualStop && localStorage.getItem('token')) {
            setTimeout(() => { if (!manualStop) startRealtime(); }, 5000);
        }
    });

    try {
        await connection.start();
    } catch (e) {
        // İlk start başarısız (ör. sunucu henüz ayakta değil) → güvenlik ağıyla tekrar dene.
        console.warn('Realtime başlatılamadı, yeniden denenecek:', e?.message || e);
        const failed = connection;
        connection = null;
        if (!manualStop && localStorage.getItem('token')) {
            setTimeout(() => { if (!manualStop) startRealtime(); }, 5000);
        }
        try { await failed.stop(); } catch { /* ignore */ }
    }
    return connection;
}

// Bağlantıyı kapatır. Logout'ta çağrılır.
export async function stopRealtime() {
    manualStop = true;
    const c = connection;
    connection = null;
    if (c) { try { await c.stop(); } catch { /* ignore */ } }
}

// Bir "event" sinyaline abone olur. handler(evt) → evt = { type, payload }. unsubscribe döner.
export function subscribeRealtime(handler) {
    eventHandlers.add(handler);
    return () => eventHandlers.delete(handler);
}

// Yeniden bağlanma sonrası tetiklenir (kaçan sinyalleri telafi için "tam tazele"). unsubscribe döner.
export function onRealtimeReconnected(handler) {
    reconnectHandlers.add(handler);
    return () => reconnectHandlers.delete(handler);
}
