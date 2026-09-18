/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      fontFamily: {
        // Figma uses Poppins throughout. Inter is the metric-compatible fallback.
        display: ['Poppins', 'Inter', 'system-ui', 'sans-serif'],
        sans: ['Poppins', 'Inter', 'system-ui', 'sans-serif'],
        mono: ['"JetBrains Mono"', 'ui-monospace', 'monospace'],
      },
      colors: {
        // Brand ramp anchored on the Figma action green #63883C.
        brand: {
          50: '#F2F6EE', 100: '#E4EDDC', 200: '#D4E0CC', 300: '#B3C99C',
          400: '#8CAA66', 500: '#63883C', 600: '#55742F', 700: '#455F26',
          800: '#374C1F', 900: '#2A3A18', 950: '#18220E',
        },
        // Navy sidebar / dark surfaces.
        navy: {
          DEFAULT: '#19233F', deep: '#131A29', line: '#2A3454',
          dim: '#A9B4CC', label: '#6B7794',
        },
        // Accent blue: links, record IDs, secondary actions.
        accent: { soft: '#CCDCFC', DEFAULT: '#5880E4', 600: '#4A6FCC', 700: '#3C6AD0' },
        ink: { DEFAULT: '#18203C', soft: '#687C88', faint: '#98A4AC' },
        surface: { DEFAULT: '#FCFCFC', muted: '#F0F6FA', sunken: '#F4F8FC' },
        line: { DEFAULT: '#E4E8EC', strong: '#D8DCE0' },
        critical: { 50: '#FBEEEE', 100: '#F7E2E2', 500: '#C4403C', 600: '#B03734', 700: '#932C2A' },
        warning: { 50: '#FBF5E8', 100: '#F6EBD4', 500: '#C08A2C', 600: '#A97724', 700: '#8B611C' },
        normal: { 50: '#EFF7EC', 100: '#DCEFD6', 500: '#4E8C3C', 600: '#437A33', 700: '#376428' },
        info: { 50: '#EDF2FC', 100: '#DCE6F8', 500: '#3C6AD0', 600: '#345CB6', 700: '#2B4C96' },
        offline: { 100: '#EDEDED', 500: '#8A8A8A' },
        live: '#2FA84F',
      },
      boxShadow: {
        card: '0 1px 2px rgba(24, 32, 60, 0.04), 0 1px 3px rgba(24, 32, 60, 0.06)',
        pop: '0 8px 24px rgba(24, 32, 60, 0.12)',
      },
      borderRadius: { xl: '0.625rem', '2xl': '0.875rem' },
      keyframes: {
        'fade-in': { '0%': { opacity: '0', transform: 'translateY(4px)' }, '100%': { opacity: '1', transform: 'none' } },
        pulseRing: { '0%': { transform: 'scale(.85)', opacity: '0.7' }, '100%': { transform: 'scale(2.2)', opacity: '0' } },
      },
      animation: { 'fade-in': 'fade-in .25s ease-out', ring: 'pulseRing 1.8s cubic-bezier(.24,.6,.35,1) infinite' },
    },
  },
  plugins: [],
}
