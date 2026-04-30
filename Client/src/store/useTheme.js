import { create } from 'zustand';

const useThemeStore = create((set) => ({
    theme: localStorage.getItem('theme') || 'dark',
    setTheme: (theme) => {
        localStorage.setItem('theme', theme);
        document.documentElement.setAttribute('data-theme', theme);
        set({ theme });
    },
    toggleTheme: () => {
        const next = localStorage.getItem('theme') === 'light' ? 'dark' : 'light';
        localStorage.setItem('theme', next);
        document.documentElement.setAttribute('data-theme', next);
        set({ theme: next });
    },
}));

// Sayfa yüklenince tema uygula
const saved = localStorage.getItem('theme') || 'dark';
document.documentElement.setAttribute('data-theme', saved);

export default useThemeStore;