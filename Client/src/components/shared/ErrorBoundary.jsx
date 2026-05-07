import { Component } from 'react';

export class ErrorBoundary extends Component {
    state = { hasError: false };

    static getDerivedStateFromError() {
        return { hasError: true };
    }

    componentDidCatch(error, info) {
        console.error('ErrorBoundary caught:', error, info);
    }

    render() {
        if (this.state.hasError) {
            return (
                <div style={{
                    minHeight: '100vh',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    background: 'var(--navy)',
                    flexDirection: 'column',
                    gap: '16px',
                }}>
                    <p style={{ color: 'var(--text-muted)', fontSize: '15px' }}>
                        Beklenmedik bir hata oluştu.
                    </p>
                    <button
                        onClick={() => this.setState({ hasError: false })}
                        style={{
                            background: 'var(--accent)',
                            color: '#fff',
                            border: 'none',
                            borderRadius: '8px',
                            padding: '8px 20px',
                            cursor: 'pointer',
                            fontSize: '14px',
                        }}>
                        Tekrar Dene
                    </button>
                </div>
            );
        }
        return this.props.children;
    }
}
