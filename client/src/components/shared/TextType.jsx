import { useEffect, useRef, useState } from 'react';

export default function TextType({
    text,
    typingSpeed = 70,
    deletingSpeed = 40,
    pauseDuration = 1800,
    showCursor = true,
    cursorCharacter = '|',
    cursorBlinkDuration = 0.6,
    loop = true,
    className = '',
    style = {},
}) {
    const texts = Array.isArray(text) ? text : [text];
    const [index, setIndex] = useState(0);
    const [display, setDisplay] = useState('');
    const [phase, setPhase] = useState('typing'); // typing | pausing | deleting
    const timerRef = useRef(null);

    useEffect(() => {
        const current = texts[index] ?? '';

        if (phase === 'typing') {
            if (display.length < current.length) {
                timerRef.current = setTimeout(
                    () => setDisplay(current.slice(0, display.length + 1)),
                    typingSpeed
                );
            } else {
                if (!loop && index === texts.length - 1) return;
                timerRef.current = setTimeout(() => setPhase('deleting'), pauseDuration);
            }
        } else if (phase === 'deleting') {
            if (display.length > 0) {
                timerRef.current = setTimeout(
                    () => setDisplay(current.slice(0, display.length - 1)),
                    deletingSpeed
                );
            } else {
                setIndex((i) => (i + 1) % texts.length);
                setPhase('typing');
            }
        }

        return () => clearTimeout(timerRef.current);
    }, [display, phase, index, texts, typingSpeed, deletingSpeed, pauseDuration, loop]);

    return (
        <span className={className} style={style}>
            {display}
            {showCursor && (
                <span
                    style={{
                        display: 'inline-block',
                        marginLeft: '2px',
                        animation: `tt-blink ${cursorBlinkDuration}s steps(1) infinite`,
                    }}
                >
                    {cursorCharacter}
                </span>
            )}
            <style>{`@keyframes tt-blink{50%{opacity:0}}`}</style>
        </span>
    );
}
