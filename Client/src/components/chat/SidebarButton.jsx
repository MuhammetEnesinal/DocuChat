// Sidebar'da tekrar eden nav butonu
export default function SidebarButton({ onClick, icon, label, active = false, danger = false }) {
    return (
        <button
            onClick={onClick}
            className="w-full flex items-center gap-2 px-3 py-2.5 rounded-xl text-sm transition-all"
            style={{ color: active ? 'white' : danger ? '#f87171' : '#94a3b8', background: active ? 'var(--accent)' : 'transparent' }}
            onMouseEnter={(e) => {
                if (!active) e.currentTarget.style.background = danger ? 'rgba(248,113,113,0.1)' : 'var(--surface2)';
            }}
            onMouseLeave={(e) => {
                if (!active) e.currentTarget.style.background = 'transparent';
            }}
        >
            {icon}
            {label}
        </button>
    );
}