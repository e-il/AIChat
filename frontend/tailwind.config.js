/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        // Ethereal Design System - Material 3 inspired
        primary: {
          DEFAULT: '#0053dc',
          dim: '#0049c2',
          container: '#3e76fe',
          fixed: '#3e76fe',
          'fixed-dim': '#2d68f0',
        },
        surface: {
          DEFAULT: '#f7f9fb',
          bright: '#f7f9fb',
          dim: '#d4dbdf',
          container: '#eaeff2',
          'container-low': '#f0f4f7',
          'container-high': '#e3e9ed',
          'container-highest': '#dce4e8',
          'container-lowest': '#ffffff',
          variant: '#dce4e8',
          tint: '#0053dc',
        },
        'on-surface': {
          DEFAULT: '#2c3437',
          variant: '#596064',
        },
        'on-primary': {
          DEFAULT: '#faf8ff',
          container: '#000000',
          fixed: '#ffffff',
          'fixed-variant': '#f9f7ff',
        },
        secondary: {
          DEFAULT: '#506076',
          dim: '#44546a',
          container: '#d3e4fe',
          fixed: '#d3e4fe',
          'fixed-dim': '#c5d6f0',
        },
        'on-secondary': {
          DEFAULT: '#f7f9ff',
          container: '#435368',
          fixed: '#314055',
          'fixed-variant': '#4d5d73',
        },
        tertiary: {
          DEFAULT: '#6d567f',
          dim: '#604a72',
          container: '#e6cafa',
        },
        outline: {
          DEFAULT: '#747c80',
          variant: '#acb3b7',
        },
        error: {
          DEFAULT: '#a83836',
          dim: '#67040d',
          container: '#fa746f',
        },
        inverse: {
          surface: '#0b0f10',
          'on-surface': '#9a9d9f',
          primary: '#618bff',
        },
        background: '#f7f9fb',
      },
      fontFamily: {
        headline: ['Manrope', 'system-ui', 'sans-serif'],
        body: ['Plus Jakarta Sans', 'system-ui', 'sans-serif'],
        label: ['Plus Jakarta Sans', 'system-ui', 'sans-serif'],
      },
      borderRadius: {
        DEFAULT: '0.25rem',
        lg: '0.5rem',
        xl: '0.75rem',
        '2xl': '1rem',
        '3xl': '1.5rem',
        '4xl': '2rem',
      },
    },
  },
  plugins: [],
}
