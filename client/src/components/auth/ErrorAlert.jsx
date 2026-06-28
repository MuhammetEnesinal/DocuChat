export default function ErrorAlert({ message }) {
    if (!message) return null;
    return (
        <div className="mb-4 p-3 rounded-lg text-sm"
            style={{ background: 'rgba(239,68,68,0.1)', border: '1px solid rgba(239,68,68,0.3)', color: '#fca5a5' }}>
            {message}
        </div>
    );
}