export default function SubmitButton({ loading, children }) {
    return (
        <button type="submit" disabled={loading}
            className="w-full py-3 rounded-xl font-semibold text-white transition-all"
            style={{ background: loading ? 'var(--navy-light)' : 'var(--accent)', cursor: loading ? 'not-allowed' : 'pointer' }}>
            {children}
        </button>
    );
}
