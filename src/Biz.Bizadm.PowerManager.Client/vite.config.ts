/// <reference types="vitest/config" />
import path from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// 개발 모드는 Tls 없이 실행한다. TLS를 켜면 SERVER_URL=https://localhost:443 으로 지정한다(secure: false).
const serverUrl = process.env.SERVER_URL ?? "http://localhost:80";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { "@": path.resolve(__dirname, "./src") } },
  server: {
    // Biz.Bizadm.PowerManager.Server.Spa의 SpaProxyServerUrl과 맞춘다. 점유 시 다른 포트로 뜨지 않게 한다.
    port: 5173,
    strictPort: true,
    proxy: {
      // React가 호출하는 경로만 둔다(장치·FOTA는 Vite를 거치지 않음).
      "/api": { target: serverUrl, changeOrigin: true, secure: false },
    },
  },
  test: { environment: "jsdom", globals: true, setupFiles: "./src/test/setup.ts" },
});
