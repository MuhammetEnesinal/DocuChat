import { create } from 'zustand';

const useThemeStore = create(() => ({
    theme: 'dark',
}));

document.documentElement.setAttribute('data-theme', 'dark');
localStorage.removeItem('theme');

export default useThemeStore;
