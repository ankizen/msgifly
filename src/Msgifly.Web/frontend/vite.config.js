import { defineConfig } from 'vite';

// No laravel-vite-plugin / manifest here — Razor views reference the fixed output filenames
// directly with the asp-append-version tag helper for cache-busting instead of manifest hashes.
export default defineConfig({
  root: '.',
  build: {
    outDir: '../wwwroot/build',
    emptyOutDir: true,
    rollupOptions: {
      input: 'resources/js/app.js',
      output: {
        entryFileNames: 'app.js',
        assetFileNames: 'app.[ext]',
      },
    },
  },
  resolve: {
    alias: {
      '@': '/resources/js',
    },
  },
});
