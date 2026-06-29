export default function SidebarButton({ onClick, icon, label, active = false, danger = false }) {
    const baseColor = active ? '#fff' : danger ? '#fca5a5' : 'rgba(255,255,255,0.92)';
    const baseBg = active
        ? 'linear-gradient(135deg, rgba(var(--accent-rgb),0.25) 0%, rgba(99,102,241,0.18) 100%)'
        : 'transparent';
    const baseBorder = active ? '1px solid rgba(var(--accent-light-rgb),0.4)' : '1px solid transparent';

    return (
        <button
            onClick={onClick}
            style={{
                width: '100%',
                display: 'flex',
                alignItems: 'center',
                gap: '10px',
                padding: '11px 14px',
                borderRadius: '12px',
                fontSize: '14px',
                fontWeight: 600,
                color: baseColor,
                background: baseBg,
                border: baseBorder,
                cursor: 'pointer',
                transition: 'all 0.15s',
                outline: 'none',
            }}
            onMouseEnter={(e) => {
                if (active) return;
                if (danger) {
                    e.currentTarget.style.background = 'rgba(239,68,68,0.12)';
                    e.currentTarget.style.color = '#fecaca';
                    e.currentTarget.style.borderColor = 'rgba(239,68,68,0.25)';
                } else {
                    e.currentTarget.style.background = 'rgba(var(--accent-light-rgb),0.12)';
                    e.currentTarget.style.color = '#fff';
                    e.currentTarget.style.borderColor = 'rgba(var(--accent-light-rgb),0.22)';
                }
            }}
            onMouseLeave={(e) => {
                if (active) return;
                e.currentTarget.style.background = 'transparent';
                e.currentTarget.style.color = baseColor;
                e.currentTarget.style.borderColor = 'transparent';
            }}
        >
            {icon}
            <span>{label}</span>
        </button>
    );
}
