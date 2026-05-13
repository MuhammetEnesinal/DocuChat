export default function IconButton({ onClick, title, hoverColor, hoverBg, disabled, children }) {
    return (
        <button
            type="button"
            onClick={onClick}
            title={title}
            aria-label={title}
            disabled={disabled}
            style={{
                padding: '6px', borderRadius: '8px', background: 'none',
                border: 'none', cursor: disabled ? 'not-allowed' : 'pointer',
                color: '#64748b', opacity: disabled ? 0.5 : 1, display: 'flex',
                alignItems: 'center', justifyContent: 'center',
            }}
            onMouseEnter={(e) => {
                if (!disabled) {
                    e.currentTarget.style.color = hoverColor ?? '#94a3b8';
                    e.currentTarget.style.background = hoverBg ?? 'transparent';
                }
            }}
            onMouseLeave={(e) => {
                e.currentTarget.style.color = '#64748b';
                e.currentTarget.style.background = 'transparent';
            }}
        >
            {children}
        </button>
    );
}
