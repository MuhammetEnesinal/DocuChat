import { useEffect, useRef } from 'react';
import { subscribeRealtime, onRealtimeReconnected } from '../lib/realtime';

// Belirli event tiplerinde (ve yeniden bağlanınca) refetch çağırır.
// Coalescing: 250 ms içinde gelen aynı tetikleri tek fetch'e indirir — çok kullanıcı aynı anda
// tazelerken "thundering herd" ve gereksiz REST turu önlenir. Bu, "sinyal + REST'ten çek"
// deseninin standart uygulamasıdır.
export function useRealtimeRefresh(types, refetch, options = {}) {
    const { onReconnect = true } = options;
    const list = Array.isArray(types) ? types : [types];
    const key = list.join('|');                 // stabil bağımlılık (dizi kimliği her render değişir)
    const refetchRef = useRef(refetch);
    refetchRef.current = refetch;

    useEffect(() => {
        const wanted = new Set(key.split('|'));
        let timer = null;
        const schedule = () => {
            if (timer) return;
            timer = setTimeout(() => { timer = null; refetchRef.current?.(); }, 250);
        };
        const unsubEvent = subscribeRealtime((evt) => {
            if (evt?.type && wanted.has(evt.type)) schedule();
        });
        const unsubReconnect = onReconnect ? onRealtimeReconnected(schedule) : null;
        return () => {
            unsubEvent();
            if (unsubReconnect) unsubReconnect();
            if (timer) clearTimeout(timer);
        };
    }, [key, onReconnect]);
}

// Ham event erişimi — payload'a göre spesifik patch yapmak isteyen hook'lar için (ör. tek id'yi
// güncelle). handler(evt) → evt = { type, payload }.
export function useRealtimeEvent(handler) {
    const ref = useRef(handler);
    ref.current = handler;
    useEffect(() => subscribeRealtime((evt) => ref.current?.(evt)), []);
}
