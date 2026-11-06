// Rust tarafıyla konuşan katman.
// Uygulama Tauri dışında (sıradan bir tarayıcıda) açılırsa, arayüzü denemek için
// bellekte çalışan sahte bir arka uç kullanılır. Sahte arka uç hiçbir şey kaydetmez.

const T = window.__TAURI__;
export const isTauri = Boolean(T);

const KDBX_FILTER = [{ name: 'KeePass kasası', extensions: ['kdbx'] }];

export function call(command, args = {}) {
  if (T) return T.core.invoke(command, args);
  return mock(command, args);
}

export async function onEvent(name, handler) {
  if (T) return T.event.listen(name, (e) => handler(e.payload));
  mockListeners[name] = handler;
  return () => {};
}

export async function pickOpenPath() {
  if (!T) return 'C:\\Deneme\\Kasa.kdbx';
  return T.dialog.open({ title: 'Kasa dosyası seç', multiple: false, directory: false, filters: KDBX_FILTER });
}

export async function pickSavePath() {
  if (!T) return 'C:\\Deneme\\Yeni.kdbx';
  return T.dialog.save({ title: 'Yeni kasanın konumu', defaultPath: 'KeyDone.kdbx', filters: KDBX_FILTER });
}

export async function openUrl(url) {
  if (T) return T.opener.openUrl(url);
  window.open(url, '_blank', 'noopener');
}

// ---------------- Sahte arka uç (yalnızca tarayıcıda deneme için) ----------------

const mockListeners = {};
const mockDb = { open: false, entries: new Map() };
let mockId = 0;

function seedMock() {
  if (mockDb.entries.size) return;
  const add = (kind, fields) => {
    const id = `mock-${++mockId}`;
    mockDb.entries.set(id, { id, kind, fields });
  };
  add('giris', [
    { key: 'Title', value: 'E-posta', protected: false },
    { key: 'UserName', value: 'ornek@posta.com', protected: false },
    { key: 'Password', value: 'aB1!xY9#', protected: true },
    { key: 'URL', value: 'https://posta.ornek.com', protected: false },
  ]);
  add('kart', [
    { key: 'Title', value: 'Maaş kartı', protected: false },
    { key: 'Kart numarası', value: '4111111111111111', protected: true },
    { key: 'PIN', value: '1234', protected: true },
  ]);
}

function mock(command, args) {
  const wait = (v) => new Promise((r) => setTimeout(() => r(v), 150));
  const fail = (m) => Promise.reject(m);
  const entry = (id) => mockDb.entries.get(id);
  switch (command) {
    case 'default_vault_path': return wait('C:\\Deneme\\KeyDone.kdbx');
    case 'vault_exists': return wait(true);
    case 'create_vault': mockDb.open = true; mockDb.entries.clear(); return wait(null);
    case 'open_vault':
      if (args.password !== 'deneme') return fail('Ana şifre yanlış ya da dosya bir KeePass kasası değil.');
      seedMock(); mockDb.open = true; return wait(null);
    case 'lock_vault': mockDb.open = false; return wait(null);
    case 'list_entries':
      return wait([...mockDb.entries.values()].map((e) => ({
        id: e.id, kind: e.kind,
        title: e.fields.find((f) => f.key === 'Title')?.value ?? '',
        username: e.fields.find((f) => f.key === 'UserName')?.value ?? '',
      })));
    case 'get_entry': {
      const e = entry(args.id);
      if (!e) return fail('Kayıt bulunamadı.');
      return wait({
        id: e.id, kind: e.kind,
        fields: e.fields.map((f) => ({
          key: f.key, protected: f.protected,
          value: f.protected ? null : f.value, hasValue: f.value !== '',
        })),
      });
    }
    case 'reveal_field': return wait(entry(args.id)?.fields.find((f) => f.key === args.key)?.value ?? '');
    case 'copy_field': {
      const v = entry(args.id)?.fields.find((f) => f.key === args.key)?.value;
      if (!v) return fail('EMPTY');
      setTimeout(() => mockListeners['clipboard-cleared']?.(), args.seconds * 1000);
      return wait(null);
    }
    case 'copy_text':
      setTimeout(() => mockListeners['clipboard-cleared']?.(), args.seconds * 1000);
      return wait(null);
    case 'save_entry': {
      const { id, kind, fields } = args.entry;
      const newId = id ?? `mock-${++mockId}`;
      mockDb.entries.set(newId, { id: newId, kind, fields });
      return wait(newId);
    }
    case 'delete_entry': mockDb.entries.delete(args.id); return wait(null);
    default: return fail(`Bilinmeyen komut: ${command}`);
  }
}
