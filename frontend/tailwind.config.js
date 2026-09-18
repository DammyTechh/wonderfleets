/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      fontFamily: {
        // Plus Jakarta Sans for headings, Inter for interface text, JetBrains Mono for codes and readings.
        display: ['"Plus Jakarta Sans"', 'system-ui', 'sans-serif'],
        sans: ['Inter', 'system-ui', 'sans-serif'],
        mono: ['"JetBrains Mono"', 'ui-monospace', 'monospace'],
      },
      colors: {
        brand: {
          50: '#eefbf3', 100: '#d6f5e2', 200: '#b0e9c9', 300: '#7ed8a9',
          400: '#46bf85', 500: '#22a56b', 600: '#158554', 700: '#126a46',
          800: '#12543a', 900: '#0f5132', 950: '#05261a',
        },
        ink: { DEFAULT: '#1f2a24', soft: '#5c6b63', faint: '#8b9891' },
        surface: { DEFAULT: '#ffffff', muted: '#f7faf8', sunken: '#f1f5f3' },
        line: { DEFAULT: '#e6ece9', strong: '#d3ded8' },
        critical: { 50: '#fef3f2', 100: '#fee4e2', 500: '#f04438', 600: '#d92d20', 700: '#b42318' },
        warning: { 50: '#fffaeb', 100: '#fef0c7', 500: '#f79009', 600: '#dc6803', 700: '#b54708' },
        info: { 50: '#eff8ff', 100: '#d1e9ff', 500: '#2e90fa', 600: '#1570ef', 700: '#175cd3' },
      },
      boxShadow: {
        card: '0 1px 2px rgba(16, 32, 24, 0.06), 0 1px 3px rgba(16, 32, 24, 0.04)',
        pop: '0 12px 32px -8px rgba(16, 32, 24, 0.18)',
      },
      borderRadius: { xl: '0.875rem', '2xl': '1.125rem' },
      keyframes: {
        'fade-in': { '0%': { opacity: '0', transform: 'translateY(4px)' }, '100%': { opacity: '1', transform: 'none' } },
        pulseRing: { '0%': { transform: 'scale(.85)', opacity: '0.7' }, '100%': { transform: 'scale(2.2)', opacity: '0' } },
      },
      animation: { 'fade-in': 'fade-in .25s ease-out', ring: 'pulseRing 1.8s cubic-bezier(.24,.6,.35,1) infinite' },
    },
  },
  plugins: [],
}
