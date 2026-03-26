const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('usurper', {
  getConfig: () => ipcRenderer.invoke('get-config'),

  // Local game process
  spawnGame: () => ipcRenderer.invoke('spawn-game'),
  sendInput: (data) => ipcRenderer.send('game-input', data),
  onGameData: (callback) => ipcRenderer.on('game-data', (_event, data) => callback(data)),
  onGameExit: (callback) => ipcRenderer.on('game-exit', (_event, code) => callback(code)),
  onGameError: (callback) => ipcRenderer.on('game-error', (_event, msg) => callback(msg)),
});
