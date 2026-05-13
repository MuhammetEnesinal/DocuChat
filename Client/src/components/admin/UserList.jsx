import SearchInput from '../shared/SearchInput';
import { UserSkeleton } from '../shared/Skeleton';
import { formatDate } from '../../utils/format';
import IconButton from '../shared/IconButton';
import Spinner from '../shared/Spinner';

export default function UserList({ users, loading, search, onSearchChange, onAdd, onEdit, onDelete, deletingUserId }) {
    const filtered = users.filter(u =>
        u.fullName.toLowerCase().includes(search.toLowerCase()) ||
        u.email.toLowerCase().includes(search.toLowerCase())
    );

    return (
        <div style={{ borderRadius: '16px', overflow: 'hidden', background: 'var(--surface)', border: '1px solid var(--border)' }}>
            <div className="admin-list-header" style={{ padding: '16px 20px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', borderBottom: '1px solid var(--border)', flexWrap: 'wrap' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                    <h2 style={{ fontSize: '15px', fontWeight: 600, color: 'var(--text-primary)', margin: 0 }}>Kullanıcılar</h2>
                    <span style={{ fontSize: '13px', color: 'var(--gray-light)' }}>{users.length} kullanıcı</span>
                </div>
                <div className="admin-users-actions" style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                    <div style={{ flex: 1, minWidth: '150px' }}>
                        <SearchInput value={search} onChange={onSearchChange} placeholder="Kullanıcı ara..." />
                    </div>
                    <button onClick={onAdd}
                        style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '8px 14px', borderRadius: '10px', fontSize: '14px', fontWeight: 500, color: 'var(--text-primary)', background: 'var(--accent)', border: 'none', cursor: 'pointer', whiteSpace: 'nowrap' }}
                        onMouseEnter={(e) => e.currentTarget.style.opacity = '0.85'}
                        onMouseLeave={(e) => e.currentTarget.style.opacity = '1'}>
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                            <line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" />
                        </svg>
                        Ekle
                    </button>
                </div>
            </div>

            {loading ? <UserSkeleton /> : filtered.length === 0 ? (
                <div style={{ textAlign: 'center', padding: '48px 16px' }}>
                    <p style={{ fontSize: '14px', color: 'var(--gray-light)' }}>{search ? 'Sonuç bulunamadı' : 'Kullanıcı bulunamadı'}</p>
                </div>
            ) : filtered.map((u) => {
                const isAdmin = u.roles?.includes('Admin');
                return (
                    <div key={u.id} style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '14px 20px', borderBottom: '1px solid var(--border)', transition: 'background 0.15s' }}
                        onMouseEnter={(e) => e.currentTarget.style.background = 'var(--surface2)'}
                        onMouseLeave={(e) => e.currentTarget.style.background = 'transparent'}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '12px', minWidth: 0 }}>
                            <div style={{ width: '36px', height: '36px', borderRadius: '50%', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0, fontWeight: 600, fontSize: '14px', background: isAdmin ? 'rgba(59,130,246,0.2)' : 'rgba(100,116,139,0.2)', color: isAdmin ? '#93c5fd' : '#94a3b8' }}>
                                {u.fullName?.charAt(0)?.toUpperCase() || '?'}
                            </div>
                            <div style={{ minWidth: 0 }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                                    <p style={{ fontSize: '14px', fontWeight: 500, color: 'var(--text-primary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', margin: 0 }}>{u.fullName}</p>
                                    {isAdmin && <span style={{ fontSize: '11px', padding: '2px 8px', borderRadius: '6px', fontWeight: 500, background: 'rgba(59,130,246,0.15)', color: '#93c5fd', border: '1px solid rgba(59,130,246,0.2)', flexShrink: 0 }}>Admin</span>}
                                </div>
                                <p style={{ fontSize: '12px', color: 'var(--gray-light)', marginTop: '2px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{u.email} · {formatDate(u.createdAt)}</p>
                            </div>
                        </div>
                        {!isAdmin && (
                            <div style={{ display: 'flex', gap: '4px', flexShrink: 0, marginLeft: '8px' }}>
                                <IconButton onClick={() => onEdit(u)} title="Düzenle" hoverColor="#93c5fd" hoverBg="rgba(59,130,246,0.1)">
                                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                        <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
                                        <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
                                    </svg>
                                </IconButton>
                                <IconButton onClick={() => deletingUserId !== u.id && onDelete(u.id, u.fullName)} title={deletingUserId === u.id ? 'Siliniyor...' : 'Sil'} hoverColor="#f87171" hoverBg="rgba(248,113,113,0.1)" disabled={deletingUserId === u.id}>
                                    {deletingUserId === u.id
                                        ? <Spinner size={15} />
                                        : <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                            <polyline points="3 6 5 6 21 6" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
                                          </svg>
                                    }
                                </IconButton>
                            </div>
                        )}
                    </div>
                );
            })}
        </div>
    );
}