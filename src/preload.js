const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('rocket', {
  getOrders: () => ipcRenderer.invoke('get-orders'),
  refresh: () => ipcRenderer.invoke('refresh-orders'),
  getStatus: () => ipcRenderer.invoke('get-status'),
  onOrders: (callback) => ipcRenderer.on('orders', (_event, orders) => callback(orders)),
  onStatus: (callback) => ipcRenderer.on('status', (_event, status) => callback(status)),
});
