export default function FormInput({
    label, type = 'text', value, onChange,
    placeholder, required = false,
    hint, error, suffix,
    style: extraStyle = {}
}) {
    return (
        <div>
            {label && (
                <label className="block text-sm font-medium mb-2" style={{ color: '#94a3b8' }}>{label}</label>
            )}
            <div className="relative">
                <input
                    type={type}
                    value={value}
                    onChange={onChange}
                    required={required}
                    placeholder={placeholder}
                    className="w-full px-4 py-3 rounded-xl text-white placeholder-slate-500 outline-none transition-all"
                    style={{
                        background: 'var(--surface2)',
                        border: `1px solid ${error ? 'rgba(248,113,113,0.5)' : 'var(--border)'}`,
                        fontSize: '0.9rem',
                        paddingRight: suffix ? '48px' : undefined,
                        WebkitAppearance: 'none',
                        ...extraStyle,
                    }}
                    onFocus={(e) => e.target.style.borderColor = error ? 'rgba(248,113,113,0.5)' : 'var(--accent)'}
                    onBlur={(e) => e.target.style.borderColor = error ? 'rgba(248,113,113,0.5)' : 'var(--border)'}
                />
                {suffix && (
                    <div style={{ position: 'absolute', right: '12px', top: '50%', transform: 'translateY(-50%)' }}>
                        {suffix}
                    </div>
                )}
            </div>
            {hint && <p className="text-xs mt-1.5" style={{ color: '#475569' }}>{hint}</p>}
            {error && <p className="text-xs mt-1.5" style={{ color: '#f87171' }}>{error}</p>}
        </div>
    );
}