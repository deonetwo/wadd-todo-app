# Wadd ToDo - Cloudflare Worker OAuth 2.0 Proxy

Serverless OAuth 2.0 proxy for Wadd Desktop to securely store the Google OAuth `Client Secret` and handle permanent token issuance and renewals without exposing credentials in the client app.

---

## 🔑 Langkah 1: Cara Mendapatkan Google Client Secret dari Firebase / Google Cloud

Karena Anda sudah setup **Firebase**, Google Client Secret sudah otomatis dibuat oleh Google!

### Cara Tercepat (Langsung dari Firebase Console):
1. Buka [Firebase Console](https://console.firebase.google.com/) dan pilih project Anda.
2. Di menu kiri, pilih **Authentication** > tab **Sign-in method**.
3. Klik baris provider **Google** untuk membukanya.
4. Klik/expand **Web SDK configuration**:
   - Di sana ada **Web client ID** (contoh: `1234567890-xxx.apps.googleusercontent.com`).
   - Di bawahnya ada **Web client secret** (contoh: `GOCSPX-xxxxxxxxxxxxxx`).
5. Salin (**Copy**) nilai **Web client secret** tersebut.

*(Alternatif via Google Cloud: Buka [Google Cloud Console Credentials](https://console.cloud.google.com/apis/credentials) > cari "Web client (auto created by Google Service)" > copy Client Secret).*

---

## 🚀 Langkah 2: Deploy Cloudflare Worker (Gratis & 2 Menit)

### Opsi A: Melalui Browser / Dashboard Cloudflare (Tanpa Perlu Install Node.js)
1. Buka [dash.cloudflare.com](https://dash.cloudflare.com/) (Daftar/Login akun Cloudflare gratis).
2. Di sidebar kiri, klik **Workers & Pages** > klik tombol **Create application** > pilih **Create Worker**.
3. Beri nama worker (misal: `wadd-oauth-proxy`) lalu klik **Deploy**.
4. Klik tombol **Edit code**:
   - Hapus semua kode default.
   - Buka file [`src/index.js`](./src/index.js) di repo ini, copy semua kodenya, dan paste ke editor Cloudflare.
   - Klik tombol **Deploy** di pojok kanan atas.
5. Menambahkan Secret:
   - Kembali ke halaman detail Worker Anda (klik nama worker di kiri atas).
   - Masuk ke tab **Settings** > **Variables and Secrets**.
   - Di bagian **Secrets**, klik **Add**:
     - Variable name: `GOOGLE_CLIENT_SECRET`
     - Value: Paste client secret `GOCSPX-...` yang Anda copy dari Firebase tadi.
     - Klik **Save and deploy**.
6. Salin URL Worker Anda (misal: `https://wadd-oauth-proxy.yourname.workers.dev`).

---

### Opsi B: Menggunakan CLI (Wrangler)
```bash
cd serverless/cloudflare-worker
npm install -g wrangler
wrangler login
wrangler secret put GOOGLE_CLIENT_SECRET # Masukkan GOCSPX-...
wrangler deploy
```

---

## ⚙️ Langkah 3: Sambungkan ke Wadd Desktop

Setelah worker Anda aktif dan memiliki URL:

1. Di file `.env` project Wadd (atau via Environment Variables):
   ```env
   GOOGLE_CLIENT_ID="1234567890-xxx.apps.googleusercontent.com"
   OAUTH_PROXY_URL="https://wadd-oauth-proxy.yourname.workers.dev"
   ```
2. Buka Wadd Desktop > **Settings** > klik **Connect Google Drive Account**.
3. **Selesai!** Wadd akan mendapatkan `RefreshToken` permanen dari Cloudflare Worker, dan sinkronisasi Google Drive akan diperbarui secara otomatis di background selamanya tanpa expire.
