import defaultTheme from 'tailwindcss/defaultTheme';
import forms from '@tailwindcss/forms';
import typography from '@tailwindcss/typography';
import aspectRatio from '@tailwindcss/aspect-ratio';

/** @type {import('tailwindcss').Config} */
export default {
  darkMode: 'class',
  content: ['../Views/**/*.cshtml', './resources/**/*.js'],
  theme: {
    extend: {
      animation: {
        'slow-ping': 'ping 2s linear infinite',
      },
      screens: {
        xs: { max: '360px' },
        xss: { min: '400px' },
      },
      fontFamily: {
        sans: ['Inter', ...defaultTheme.fontFamily.sans],
        display: ['Lexend', ...defaultTheme.fontFamily.sans],
      },
    },
  },
  // NOTE: the original declared `plugins` twice (a bug — the second assignment silently
  // dropped typography/aspect-ratio even though both were installed). Fixed here.
  plugins: [forms, typography, aspectRatio],
};
