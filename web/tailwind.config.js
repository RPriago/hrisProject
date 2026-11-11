/** @type {import('tailwindcss').Config} */
export default {
  darkMode: ['class', '[data-theme="dark"]'],
  content: [
    './index.html',
    './src/**/*.{js,ts,jsx,tsx}',
  ],
  theme: {
    extend: {
      colors: {
        // Slate-Charcoal
        'hero-base': '#1F2227',
        'hero-slate': '#353D43',
        // Midnight Navy
        'navy-action': '#0D1B39',
        'navy-action-hover': '#162B57',
        // Warm Cognac-Amber
        'amber-fill': '#E58411',
        'amber-fill-hover': '#D4760A',
        'amber-text': '#9A4A06',
        'amber-text-hover': '#7C3A04',
        'amber-rich': '#B45309',
        'amber-ink': '#14171C',
        // Canvas & Surfaces
        'canvas-light': '#FFFFFF',
        'canvas-well': '#F5F6F8',
        'surface-soft': '#F7F7F7',
        'card-stage': '#FAFAFA',
        // Text
        'text-primary': '#1E1E1E',
        'text-secondary': '#52565E',
        'text-tertiary': '#636872',
        // Clean Modern SaaS Brand Tokens
        'brand-blue': '#0062FF',
        'brand-blue-hover': '#0052D9',
        'brand-blue-light': '#EFF6FF',
        'brand-violet': '#7C3AED',
        'brand-purple': '#6D28D9',
        'brand-purple-light': '#F5F3FF',
        'saas-border': '#E2E8F0',
        'saas-border-light': '#F1F5F9',
      },
      fontFamily: {
        sans: ['"Inter"', '"Plus Jakarta Sans"', '-apple-system', 'BlinkMacSystemFont', '"Segoe UI"', 'sans-serif'],
        mono: ['"JetBrains Mono"', 'monospace'],
      },
      borderRadius: {
        'pill': '9999px',
        'stage-card': '20px',
        'inset-card': '18px',
        'collage-sm': '16px',
        'card': '18px',
      },
      boxShadow: {
        'card-soft': '0 12px 32px rgba(30, 30, 30, 0.06)',
        'floating-control': '0 6px 18px rgba(13, 27, 57, 0.12)',
        'warm-floor': '0 28px 44px -14px rgba(154, 74, 6, 0.22)',
        'dash-card': '0 1px 3px rgba(16, 24, 40, 0.04)',
        'dash-tooltip': '0 10px 26px rgba(13, 27, 57, 0.12)',
        'saas-card': '0 4px 20px -2px rgba(15, 23, 42, 0.05), 0 2px 6px -1px rgba(15, 23, 42, 0.03)',
        'saas-card-hover': '0 12px 30px -4px rgba(15, 23, 42, 0.08), 0 4px 10px -2px rgba(15, 23, 42, 0.04)',
        'hero-mockup': '0 25px 60px -15px rgba(15, 23, 42, 0.12), 0 0 0 1px rgba(15, 23, 42, 0.06)',
      },
    },
  },
  plugins: [],
};
