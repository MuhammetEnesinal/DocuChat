// Yeniden kullanılabilir skeleton loading bileşenleri

export function SkeletonLine({ width = '100%', height = '14px', style = {} }) {
    return (
        <div style={{
            width, height, borderRadius: '6px',
            background: 'linear-gradient(90deg, #1e3a5f 25%, #1d3a6e 50%, #1e3a5f 75%)',
            backgroundSize: '200% 100%',
            animation: 'shimmer 1.5s infinite',
            ...style,
        }} />
    );
}

export function SessionSkeleton() {
    return (
        <div style={{ padding: '0 12px' }}>
            <style>{`@keyframes shimmer { 0%{background-position:200% 0} 100%{background-position:-200% 0} }`}</style>
            {[80, 65, 90, 55, 70].map((w, i) => (
                <div key={i} style={{
                    padding: '10px 12px', borderRadius: '12px', marginBottom: '4px',
                    display: 'flex', alignItems: 'center', gap: '8px',
                }}>
                    <SkeletonLine width={`${w}%`} height="13px" />
                </div>
            ))}
        </div>
    );
}

export function DocumentSkeleton() {
    return (
        <div>
            <style>{`@keyframes shimmer { 0%{background-position:200% 0} 100%{background-position:-200% 0} }`}</style>
            {[1, 2, 3].map((i) => (
                <div key={i} style={{
                    display: 'flex', alignItems: 'center', gap: '16px',
                    padding: '16px 24px',
                    borderBottom: '1px solid #1e3a5f',
                }}>
                    <div style={{
                        width: '36px', height: '36px', borderRadius: '8px',
                        background: '#162440', flexShrink: 0,
                    }} />
                    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: '8px' }}>
                        <SkeletonLine width="55%" height="13px" />
                        <SkeletonLine width="30%" height="11px" />
                    </div>
                    <SkeletonLine width="60px" height="26px" style={{ borderRadius: '8px' }} />
                </div>
            ))}
        </div>
    );
}

export function UserSkeleton() {
    return (
        <div>
            {[1, 2, 3].map((i) => (
                <div key={i} style={{
                    display: 'flex', alignItems: 'center', gap: '16px',
                    padding: '16px 24px',
                    borderBottom: '1px solid #1e3a5f',
                }}>
                    <div style={{
                        width: '36px', height: '36px', borderRadius: '50%',
                        background: '#162440', flexShrink: 0,
                    }} />
                    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: '8px' }}>
                        <SkeletonLine width="40%" height="13px" />
                        <SkeletonLine width="55%" height="11px" />
                    </div>
                    <SkeletonLine width="50px" height="26px" style={{ borderRadius: '8px' }} />
                </div>
            ))}
        </div>
    );
}

export function MessageSkeleton() {
    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', padding: '24px 0' }}>
            <style>{`@keyframes shimmer { 0%{background-position:200% 0} 100%{background-position:-200% 0} }`}</style>
            {/* User message */}
            <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                <div style={{ maxWidth: '60%', display: 'flex', flexDirection: 'column', gap: '6px', alignItems: 'flex-end' }}>
                    <SkeletonLine width="180px" height="36px" style={{ borderRadius: '16px 16px 4px 16px' }} />
                </div>
            </div>
            {/* Assistant message */}
            <div style={{ display: 'flex', gap: '12px' }}>
                <div style={{ width: '32px', height: '32px', borderRadius: '50%', background: '#162440', flexShrink: 0 }} />
                <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    <SkeletonLine width="90%" height="13px" />
                    <SkeletonLine width="75%" height="13px" />
                    <SkeletonLine width="60%" height="13px" />
                </div>
            </div>
            {/* User message */}
            <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                <div style={{ maxWidth: '60%', display: 'flex', flexDirection: 'column', gap: '6px', alignItems: 'flex-end' }}>
                    <SkeletonLine width="220px" height="36px" style={{ borderRadius: '16px 16px 4px 16px' }} />
                </div>
            </div>
            {/* Assistant message */}
            <div style={{ display: 'flex', gap: '12px' }}>
                <div style={{ width: '32px', height: '32px', borderRadius: '50%', background: '#162440', flexShrink: 0 }} />
                <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    <SkeletonLine width="85%" height="13px" />
                    <SkeletonLine width="70%" height="13px" />
                    <SkeletonLine width="50%" height="13px" />
                    <SkeletonLine width="40%" height="13px" />
                </div>
            </div>
        </div>
    );
}
