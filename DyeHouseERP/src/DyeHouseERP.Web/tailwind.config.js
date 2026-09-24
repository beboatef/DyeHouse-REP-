/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{js,ts,jsx,tsx}"],
  theme: {
    extend: {
      colors: {
        brand: {
          50: "#eef6ff",
          100: "#d9ecff",
          500: "#2f6fed",
          600: "#255bc4",
          700: "#1d479b"
        }
      }
    }
  },
  plugins: []
};
