// Messages between the page and the game (C#). Inside WebView2 the game answers; in a plain browser nothing does.
const webview = window.chrome?.webview;

export const bridge = {
  attached: !!webview,

  post(message) {
    webview?.postMessage(message);
  },

  onMessage(handler) {
    webview?.addEventListener('message', e => handler(typeof e.data === 'string' ? JSON.parse(e.data) : e.data));
  }
};
