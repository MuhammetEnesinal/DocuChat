export default function FormInput({
    label, type = 'text', value, onChange,
    placeholder, required = false,
    hint, error, suffix,
    style: extraStyle = {}
}) {
    return (
        <div>
            {label && (
                <label className="block text-sm font-medium mb-2" style={{ color: '#cbd5e1' }}>{label}</label>
            )}
            <div className="relative">
                <input
                    type={type}
                    value={value}
                    onChange={onChange}
                    required={required}
                    placeholder={placeholder}
                    className="w-full px-4 py-3 rounded-xl outline-none transition-all"
                    style={{
                        background: 'rgba(255,255,255,0.04)',
                        border: `1px solid ${error ? 'rgba(248,113,113,0.5)' : 'rgba(255,255,255,0.1)'}`,
                        color: '#f1f5f9',
                        fontSize: '0.92rem',
                        paddingRight: suffix ? '48px' : undefined,
                        WebkitAppearance: 'none',
                        ...extraStyle,
                    }}
                    onFocus={(e) => {
                        e.target.style.borderColor = error ? 'rgba(248,113,113,0.5)' : 'var(--accent-deep)';
                        e.target.style.boxShadow = error
                            ? '0 0 0 3px rgba(248,113,113,0.12)'
                            : '0 0 0 3px rgba(99,102,241,0.2)';
                        e.target.style.background = 'rgba(255,255,255,0.06)';
                    }}
                    onBlur={(e) => {
                        e.target.style.borderColor = error ? 'rgba(248,113,113,0.5)' : 'rgba(255,255,255,0.1)';
                        e.target.style.boxShadow = 'none';
                        e.target.style.background = 'rgba(255,255,255,0.04)';
                    }}
                />
                {suffix && (
                    <div style={{ position: 'absolute', right: '12px', top: '50%', transform: 'translateY(-50%)' }}>
                        {suffix}
                    </div>
                )}
            </div>
            {hint && <p className="text-xs mt-1.5" style={{ color: '#64748b' }}>{hint}</p>}
            {error && <p className="text-xs mt-1.5" style={{ color: '#f87171' }}>{error}</p>}
        </div>
    );
}
