import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
// Bundle analizi için: npx vite-bundle-visualizer (ayrı kurulum gerekmez)
export default defineConfig({
  plugins: [react()],
  build: {
    // Chunk boyutu uyarı limiti: 500KB
    chunkSizeWarningLimit: 500,
    rollupOptions: {
      output: {
        // Manuel chunk split: büyük kütüphaneleri ayır
        manualChunks: (id) => {
          if (id.includes('react-dom') || id.includes('react-router-dom')) return 'react-vendor';
          if (id.includes('react-markdown') || id.includes('remark-gfm') || id.includes('react-syntax-highlighter')) return 'markdown';
          if (id.includes('zustand') || id.includes('@tanstack')) return 'state';
        },
      },
    },
  },
})
