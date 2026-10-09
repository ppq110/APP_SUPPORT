const { app, BrowserWindow, shell, ipcMain } = require('electron');
const path = require('path');

const APP_TITLE = 'BIMLab Convert TXT To Excel';
const APP_W     = 1280;
const APP_H     = 820;
const isDev     = process.env.VITE_DEV_SERVER_URL;

// Hardcoded intentionally: end users must not be able to redirect the auth
// check to a server they control (no env var / config file override).
// Only a new build can change this.
const AUTH_SERVER_URL = 'https://tools.bimlab.com.vn';

app.setName(APP_TITLE);

// Fire-and-forget as soon as the process starts, well before the login
// screen has even rendered. Node's fetch keeps a keep-alive connection pool
// per host, so this warms the TCP/TLS handshake to tools.bimlab.com.vn
// ahead of time - by the time the user types an email and clicks
// "Đăng nhập", the real check only pays for the request itself. Hits
// /api/health (not rate-limited, not proxied to the backend - nginx
// answers 404 directly) purely to warm the connection.
fetch(`${AUTH_SERVER_URL}/api/health`).catch(() => {});

// Runs in the trusted main process (not the sandboxed renderer) so the
// request isn't subject to browser CORS and the server URL can't be seen
// or swapped by anything running in the page.
ipcMain.handle('auth:check', async (_event, rawEmail) => {
  const email = String(rawEmail || '').trim().toLowerCase();
  if (!email || !email.includes('@') || !email.includes('.')) {
    return { ok: false, error: 'Gmail không hợp lệ.' };
  }

  try {
    const res = await fetch(`${AUTH_SERVER_URL}/api/auth/check?email=${encodeURIComponent(email)}`, {
      headers: { Accept: 'application/json' },
    });

    if (res.status === 404) {
      return { ok: false, error: 'Gmail này chưa được cấp quyền truy cập.' };
    }
    if (!res.ok) {
      return { ok: false, error: `Máy chủ xác thực trả lỗi (${res.status}).` };
    }

    const data = await res.json();
    if (!data.isActive) {
      return { ok: false, error: 'Gmail này chưa được cấp quyền truy cập.' };
    }

    return { ok: true, email: data.email || email, fullName: data.fullName || '' };
  } catch (err) {
    return { ok: false, error: 'Không kết nối được máy chủ xác thực: ' + err.message };
  }
});

function createWindow() {
  const mainWindow = new BrowserWindow({
    width: APP_W,
    height: APP_H,
    minWidth: 960,
    minHeight: 620,
    show: false,
    title: APP_TITLE,
    icon: path.join(__dirname, '..', 'build', 'IgBl.ico'),
    backgroundColor: '#eef2f7',
    autoHideMenuBar: true,
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      preload: path.join(__dirname, 'preload.cjs'),
    },
  });

  const handleAppReady = (event) => {
    if (event.sender !== mainWindow.webContents) return;
    mainWindow.center();
    mainWindow.show();
  };

  ipcMain.on('app-ready', handleAppReady);

  mainWindow.on('closed', () => {
    ipcMain.removeListener('app-ready', handleAppReady);
  });

  mainWindow.on('page-title-updated', (event) => {
    event.preventDefault();
    mainWindow.setTitle(APP_TITLE);
  });

  if (isDev) {
    mainWindow.loadURL(isDev);
  } else {
    mainWindow.loadFile(path.join(__dirname, '..', 'dist', 'index.html'));
  }

  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    if (/^https?:\/\//i.test(url) || /^mailto:/i.test(url)) {
      shell.openExternal(url);
    }
    return { action: 'deny' };
  });
}

app.whenReady().then(() => {
  createWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createWindow();
    }
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit();
  }
});
