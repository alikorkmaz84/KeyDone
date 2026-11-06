// Ekran okuyucuya duyuru gönderir.
// Aynı mesaj art arda gönderilse bile okunması için önce bölge boşaltılır.

let timer = null;

export function announce(message, { assertive = false } = {}) {
  const el = document.getElementById(assertive ? 'live-assertive' : 'live-polite');
  if (!el) return;
  el.textContent = '';
  clearTimeout(timer);
  timer = setTimeout(() => { el.textContent = message; }, 60);
}
