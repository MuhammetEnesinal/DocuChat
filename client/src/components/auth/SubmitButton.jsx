export default function SubmitButton({ loading, children }) {
    return (
        <button type="submit" disabled={loading} className="btn btn-primary btn-lg w-full" style={{ fontWeight: 600 }}>
            {children}
        </button>
    );
}
