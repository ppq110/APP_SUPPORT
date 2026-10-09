const { contextBridge, ipcRenderer } = require('electron');
contextBridge.exposeInMainWorld('electronBridge', {
  appReady: () => ipcRenderer.send('app-ready'),
  checkAuth: (email) => ipcRenderer.invoke('auth:check', email),
});
