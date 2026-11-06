import { call, onEvent, pickOpenPath, pickSavePath, openUrl } from './api.js';
import { announce } from './announce.js';
import { renderSpell } from './spell.js';
import { generatePassword, estimateBits, strengthText } from './generator.js';
import { KINDS, DEFAULT_KIND, kindOf, kindLabel, fieldDef, fieldLabel, primaryKey } from './templates.js';

const $ = (id) => document.getElementById(id);
const isMobile = /Android|iPhone|iPad|iPod/i.test(navigator.userAgent);
if (isMobile) document.body.classList.add('mobile');

// ---------------- Ayarlar (yalnızca bu cihazda, gizli bilgi içermez) ----------------

function readStorage(key) {
  try { return localStorage.getItem(key); } catch { return null; }
}
function writeStorage(key, value) {
  try { localStorage.setItem(key, value); } catch { /* depolama kapalıysa sessizce geç */ }
}

const settings = { autoLockMinutes: 5, clipboardSeconds: 30, phonetic: false };
try { Object.assign(settings, JSON.parse(readStorage('keydone.settings')) || {}); } catch { /* varsayılanlar */ }
const saveSettings = () => writeStorage('keydone.settings', JSON.stringify(settings));

// ---------------- Durum ----------------

const state = {
  unlocked: false,
  path: null,
  entries: [],   // özetler: { id, kind, title, username }
  visible: [],
  current: null, // açık kaydın ayrıntısı
  revealed: new Set(),
  editing: null, // { id, kind, dirty }
};

// ---------------- Ortak yardımcılar ----------------

const SCREENS = ['screen-unlock', 'screen-create', 'screen-vault', 'screen-edit'];

function showScreen(id, focusTarget, title) {
  for (const s of SCREENS) $(s).hidden = s !== id;
  document.title = title ? `${title} – KeyDone` : 'KeyDone';
  if (focusTarget === false) return;
  const target = focusTarget || $(id).querySelector('h1');
  requestAnimationFrame(() => target.focus());
}

function fileName(path) {
  return path ? path.split(/[\\/]/).pop() : '';
}

function setError(inputId, errorId, message) {
  const err = $(errorId);
  err.textContent = message;
  err.hidden = false;
  $(inputId)?.setAttribute('aria-invalid', 'true');
}

function clearError(inputId, errorId) {
  const err = $(errorId);
  err.textContent = '';
  err.hidden = true;
  $(inputId)?.removeAttribute('aria-invalid');
}

function el(tag, props = {}, ...children) {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(props)) {
    if (v === undefined || v === null || v === false) continue;
    if (k === 'text') node.textContent = v;
    else if (k === 'className') node.className = v;
    else if (k === 'dataset') Object.assign(node.dataset, v);
    else if (k.startsWith('on')) node.addEventListener(k.slice(2), v);
    else node.setAttribute(k, v === true ? '' : v);
  }
  node.append(...children.filter(Boolean));
  return node;
}

function debounce(fn, ms) {
  let t;
  return (...args) => { clearTimeout(t); t = setTimeout(() => fn(...args), ms); };
}

function errorText(err) {
  return typeof err === 'string' ? err : (err?.message ?? 'Beklenmeyen bir hata oluştu.');
}

function confirmDialog(heading, text, yesLabel) {
  return new Promise((resolve) => {
    const dlg = $('dlg-confirm');
    $('confirm-heading').textContent = heading;
    $('confirm-text').textContent = text;
    $('confirm-yes').textContent = yesLabel;
    const done = (value) => {
      $('confirm-yes').onclick = null;
      $('confirm-no').onclick = null;
      dlg.oncancel = null;
      dlg.close();
      resolve(value);
    };
    $('confirm-yes').onclick = () => done(true);
    $('confirm-no').onclick = () => done(false);
    dlg.oncancel = (e) => { e.preventDefault(); done(false); };
    dlg.showModal();
    $('confirm-no').focus(); // güvenli seçenek önce
  });
}

// ---------------- Kilit ekranı ----------------

async function showUnlock(message) {
  state.unlocked = false;
  let path = readStorage('keydone.lastPath');
  if (!path && isMobile) path = await call('default_vault_path');
  state.path = path;

  const info = $('unlock-file-info');
  let exists = false;
  if (path) exists = await call('vault_exists', { path }).catch(() => false);

  if (path && exists) {
    info.textContent = isMobile ? 'Kasanızı açmak için ana şifrenizi yazın.' : `Kasa dosyası: ${path}`;
    $('unlock-form').hidden = false;
  } else if (isMobile) {
    return showCreate();
  } else {
    info.textContent = path
      ? `Son kullanılan kasa bulunamadı: ${path}. Başka bir kasa açabilir ya da yeni bir kasa oluşturabilirsiniz.`
      : 'Henüz bir kasa seçilmedi. Yeni bir kasa oluşturabilir ya da var olan bir KeePass kasasını açabilirsiniz.';
    $('unlock-form').hidden = true;
  }

  $('btn-open-other').textContent = path && exists ? 'Başka bir kasa dosyası aç' : 'Var olan bir kasa dosyasını aç';
  clearError('unlock-password', 'unlock-error');
  $('unlock-password').value = '';
  const formVisible = !$('unlock-form').hidden;
  showScreen('screen-unlock', formVisible ? $('unlock-password') : null, 'Kilitli');
  if (message) announce(message);
}

$('unlock-show').addEventListener('change', (e) => {
  $('unlock-password').type = e.target.checked ? 'text' : 'password';
});

$('unlock-form').addEventListener('submit', async (e) => {
  e.preventDefault();
  const input = $('unlock-password');
  if (!input.value) {
    setError('unlock-password', 'unlock-error', 'Ana şifreyi yazın.');
    announce('Ana şifreyi yazın.', { assertive: true });
    input.focus();
    return;
  }
  clearError('unlock-password', 'unlock-error');
  const button = $('unlock-submit');
  button.disabled = true;
  announce('Kasa açılıyor, lütfen bekleyin.');
  try {
    await call('open_vault', { path: state.path, password: input.value });
    input.value = '';
    $('unlock-show').checked = false;
    input.type = 'password';
    await enterVault('Kasa açıldı.');
  } catch (err) {
    setError('unlock-password', 'unlock-error', errorText(err));
    announce(errorText(err), { assertive: true });
    input.focus();
    input.select();
  } finally {
    button.disabled = false;
  }
});

$('btn-open-other').addEventListener('click', async () => {
  const path = await pickOpenPath();
  if (!path) return;
  writeStorage('keydone.lastPath', path);
  await showUnlock(`${fileName(path)} seçildi. Ana şifresini yazın.`);
});

$('btn-go-create').addEventListener('click', () => showCreate());

// ---------------- Yeni kasa ----------------

let createPath = null;

async function showCreate() {
  createPath = isMobile ? await call('default_vault_path') : null;
  $('create-path').textContent = createPath ?? 'Henüz seçilmedi.';
  for (const id of ['create-password', 'create-password2']) {
    $(id).value = '';
    $(id).type = 'password';
  }
  $('create-show').checked = false;
  $('create-strength').textContent = '';
  clearError('btn-create-choose', 'create-path-error');
  clearError('create-password', 'create-password-error');
  clearError('create-password2', 'create-password2-error');
  // Telefonda henüz kasa yoksa geri dönülecek bir ekran yoktur.
  $('btn-create-cancel').hidden = isMobile && !readStorage('keydone.lastPath');
  showScreen('screen-create', null, 'Yeni kasa');
}

$('btn-create-choose').addEventListener('click', async () => {
  const path = await pickSavePath();
  if (!path) return;
  createPath = /\.kdbx$/i.test(path) ? path : `${path}.kdbx`;
  $('create-path').textContent = createPath;
  clearError('btn-create-choose', 'create-path-error');
  announce(`Konum seçildi: ${createPath}`);
});

const updateStrength = () => {
  const v = $('create-password').value;
  $('create-strength').textContent = v ? `Tahmini güç: ${strengthText(estimateBits(v))}` : '';
};
const announceStrength = debounce(() => {
  if ($('create-password').value) announce($('create-strength').textContent);
}, 1200);
$('create-password').addEventListener('input', () => { updateStrength(); announceStrength(); });

$('create-show').addEventListener('change', (e) => {
  const type = e.target.checked ? 'text' : 'password';
  $('create-password').type = type;
  $('create-password2').type = type;
});

$('create-form').addEventListener('submit', async (e) => {
  e.preventDefault();
  const pw = $('create-password').value;
  const pw2 = $('create-password2').value;
  clearError('btn-create-choose', 'create-path-error');
  clearError('create-password', 'create-password-error');
  clearError('create-password2', 'create-password2-error');

  let firstError = null;
  if (!createPath) {
    setError('btn-create-choose', 'create-path-error', 'Kasa dosyası için bir konum seçin.');
    firstError ??= { el: $('btn-create-choose'), msg: 'Kasa dosyası için bir konum seçin.' };
  }
  if (Array.from(pw).length < 8) {
    setError('create-password', 'create-password-error', 'Ana şifre en az 8 karakter olmalı.');
    firstError ??= { el: $('create-password'), msg: 'Ana şifre en az 8 karakter olmalı.' };
  } else if (pw !== pw2) {
    setError('create-password2', 'create-password2-error', 'İki şifre birbirinin aynısı değil.');
    firstError ??= { el: $('create-password2'), msg: 'İki şifre birbirinin aynısı değil.' };
  }
  if (firstError) {
    announce(firstError.msg, { assertive: true });
    firstError.el.focus();
    return;
  }

  // Telefonda kayıt yeri sabittir; var olan kasanın üzerine yazmadan önce sor.
  if (isMobile && await call('vault_exists', { path: createPath })) {
    const ok = await confirmDialog(
      'Var olan kasanın üzerine yazılsın mı?',
      'Bu cihazda zaten bir kasa var. Yeni kasa oluşturulursa eski kasadaki tüm kayıtlar kalıcı olarak silinir.',
      'Üzerine yaz',
    );
    if (!ok) { $('create-password').focus(); return; }
  }

  announce('Kasa oluşturuluyor, lütfen bekleyin.');
  try {
    await call('create_vault', { path: createPath, password: pw });
    $('create-password').value = '';
    $('create-password2').value = '';
    writeStorage('keydone.lastPath', createPath);
    state.path = createPath;
    await enterVault('Kasa oluşturuldu.');
  } catch (err) {
    announce(errorText(err), { assertive: true });
    setError('create-password', 'create-password-error', errorText(err));
    $('create-password').focus();
  }
});

$('btn-create-cancel').addEventListener('click', () => showUnlock());

// ---------------- Kasa ekranı ----------------

async function enterVault(message) {
  state.unlocked = true;
  $('vault-title').textContent = `Kasa: ${fileName(state.path)}`;
  $('search').value = '';
  $('filter-kind').value = '';
  closeDetail(false);
  await refreshEntries();
  showScreen('screen-vault', $('search'), 'Kasa');
  const n = state.entries.length;
  announce(`${message} ${n ? `${n} kayıt var.` : 'Henüz kayıt yok. Yeni kayıt eklemek için Control N tuşlarına basın.'}`);
  startAutoLock();
}

async function refreshEntries() {
  state.entries = await call('list_entries');
  state.entries.sort((a, b) => (a.title || '').localeCompare(b.title || '', 'tr'));
  applyFilter();
}

function countText() {
  const total = state.entries.length;
  const shown = state.visible.length;
  if (total === 0) return 'Kasada henüz kayıt yok.';
  if (shown === total) return `${total} kayıt.`;
  if (shown === 0) return 'Aramaya uyan kayıt yok.';
  return `${shown} kayıt bulundu, toplam ${total} kayıt.`;
}

function applyFilter() {
  const q = $('search').value.trim().toLocaleLowerCase('tr');
  const kind = $('filter-kind').value;
  state.visible = state.entries.filter((e) => {
    if (kind && kindOf(e.kind) !== kind) return false;
    if (!q) return true;
    return [e.title, e.username, kindLabel(e.kind)]
      .some((s) => (s || '').toLocaleLowerCase('tr').includes(q));
  });
  renderList();
}

function entryLabel(e) {
  const parts = [e.title || 'Adsız kayıt', kindLabel(e.kind)];
  if (e.username) parts.push(e.username);
  return parts.join(', ');
}

function renderList() {
  const items = state.visible.map((e) => el('li', {},
    el('button', {
      type: 'button',
      'aria-label': entryLabel(e),
      'aria-current': state.current?.id === e.id ? 'true' : null,
      dataset: { id: e.id },
      onclick: () => openDetail(e.id),
    },
    el('span', { className: 'entry-title', text: e.title || 'Adsız kayıt' }),
    el('span', { className: 'entry-sub', text: [kindLabel(e.kind), e.username].filter(Boolean).join(' · ') }),
    )));
  $('entry-list').replaceChildren(...items);
  $('list-count').textContent = countText();
}

const announceCount = debounce(() => announce(countText()), 700);
$('search').addEventListener('input', () => { applyFilter(); announceCount(); });
$('filter-kind').addEventListener('change', () => { applyFilter(); announce(countText()); });

$('search').addEventListener('keydown', (e) => {
  if (e.key === 'Enter') {
    e.preventDefault();
    if (state.visible[0]) openDetail(state.visible[0].id);
    else announce('Aramaya uyan kayıt yok.');
  } else if (e.key === 'ArrowDown') {
    const first = $('entry-list').querySelector('button');
    if (first) { e.preventDefault(); first.focus(); }
  }
});

$('entry-list').addEventListener('keydown', (e) => {
  const buttons = [...$('entry-list').querySelectorAll('button')];
  const i = buttons.indexOf(document.activeElement);
  if (i < 0) return;
  let next = null;
  if (e.key === 'ArrowDown') next = buttons[Math.min(i + 1, buttons.length - 1)];
  else if (e.key === 'ArrowUp') next = i === 0 ? $('search') : buttons[i - 1];
  else if (e.key === 'Home') next = buttons[0];
  else if (e.key === 'End') next = buttons[buttons.length - 1];
  if (next) { e.preventDefault(); next.focus(); }
});

// ---------------- Kayıt ayrıntısı ----------------

async function openDetail(id) {
  try {
    state.current = await call('get_entry', { id });
  } catch (err) {
    announce(errorText(err), { assertive: true });
    return;
  }
  state.revealed.clear();
  hideSpell(false);
  renderDetail();
  for (const b of $('entry-list').querySelectorAll('button')) {
    b.toggleAttribute('aria-current', b.dataset.id === id);
    if (b.dataset.id === id) b.setAttribute('aria-current', 'true');
  }
  $('detail-pane').hidden = false;
  $('screen-vault').classList.add('showing-detail');
  $('detail-title').focus();
}

function closeDetail(restoreFocus = true) {
  const id = state.current?.id;
  state.current = null;
  state.revealed.clear();
  hideSpell(false);
  $('detail-fields').replaceChildren();
  $('detail-pane').hidden = true;
  $('screen-vault').classList.remove('showing-detail');
  for (const b of $('entry-list').querySelectorAll('button[aria-current]')) b.removeAttribute('aria-current');
  if (!restoreFocus) return;
  const btn = id && $('entry-list').querySelector(`button[data-id="${CSS.escape(id)}"]`);
  (btn || $('search')).focus();
}

function hasValue(f) {
  return f.protected ? f.hasValue : Boolean(f.value);
}

function renderDetail() {
  const d = state.current;
  const kind = kindOf(d.kind);
  const title = d.fields.find((f) => f.key === 'Title')?.value || 'Adsız kayıt';
  $('detail-title').textContent = title;
  $('detail-kind').textContent = kindLabel(kind);

  const order = KINDS[kind].fields.map((f) => f.key);
  const rank = (key) => (order.includes(key) ? order.indexOf(key) : 1000);
  const fields = d.fields
    .filter((f) => f.key !== 'Title' && hasValue(f))
    .sort((a, b) => rank(a.key) - rank(b.key) || a.key.localeCompare(b.key, 'tr'));

  const rows = fields.map((f) => fieldRow(kind, f));
  if (rows.length === 0) rows.push(el('p', { text: 'Bu kayıtta başka bilgi yok.' }));
  $('detail-fields').replaceChildren(...rows);
}

function fieldRow(kind, f) {
  const label = fieldLabel(kind, f.key);
  const def = fieldDef(kind, f.key);
  const valueEl = el('span', { className: 'field-value', text: f.protected ? 'Gizli' : f.value });
  const buttons = [
    el('button', {
      type: 'button', className: 'small', text: 'Kopyala', 'aria-label': `Kopyala: ${label}`,
      onclick: () => copyField(state.current.id, kind, f.key),
    }),
  ];
  if (f.protected) {
    const toggle = el('button', {
      type: 'button', className: 'small', text: 'Göster', 'aria-label': `Göster: ${label}`,
      onclick: async () => {
        if (state.revealed.has(f.key)) {
          state.revealed.delete(f.key);
          valueEl.textContent = 'Gizli';
          toggle.textContent = 'Göster';
          toggle.setAttribute('aria-label', `Göster: ${label}`);
          announce(`${label} gizlendi.`);
        } else {
          const value = await call('reveal_field', { id: state.current.id, key: f.key });
          state.revealed.add(f.key);
          valueEl.textContent = value;
          toggle.textContent = 'Gizle';
          toggle.setAttribute('aria-label', `Gizle: ${label}`);
          announce(`${label}: ${value}`);
        }
      },
    });
    buttons.push(toggle);
  }
  if (!def?.multiline && f.key !== 'Notes') {
    const spellBtn = el('button', {
      type: 'button', className: 'small', text: 'Harf harf oku', 'aria-label': `Harf harf oku: ${label}`,
      onclick: async () => {
        const value = f.protected ? await call('reveal_field', { id: state.current.id, key: f.key }) : f.value;
        showSpell(label, value, spellBtn);
      },
    });
    buttons.push(spellBtn);
  }
  if (f.key === 'URL') {
    buttons.push(el('button', {
      type: 'button', className: 'small', text: 'Siteyi aç', 'aria-label': `Siteyi aç: ${f.value}`,
      onclick: () => openSite(f.value),
    }));
  }
  return el('div', { className: 'field-row' },
    el('span', { className: 'field-label', text: label }),
    valueEl,
    el('div', { className: 'button-row' }, ...buttons));
}

let spellReturnFocus = null;

function showSpell(label, value, returnFocus) {
  spellReturnFocus = returnFocus;
  $('spell-heading').textContent = `Harf harf: ${label}, ${Array.from(value).length} karakter`;
  renderSpell($('spell-list'), value, { phonetic: settings.phonetic });
  $('spell-region').hidden = false;
  $('spell-heading').focus();
}

function hideSpell(restoreFocus = true) {
  if ($('spell-region').hidden) return;
  $('spell-region').hidden = true;
  $('spell-list').replaceChildren();
  if (restoreFocus && spellReturnFocus?.isConnected) spellReturnFocus.focus();
}

$('btn-spell-close').addEventListener('click', () => hideSpell());

async function copyField(id, kind, key) {
  const label = fieldLabel(kind, key);
  try {
    await call('copy_field', { id, key, seconds: settings.clipboardSeconds });
    announce(`${label} kopyalandı. ${settings.clipboardSeconds} saniye sonra panodan silinecek.`);
  } catch (err) {
    announce(errorText(err) === 'EMPTY' ? `${label} bu kayıtta boş.` : errorText(err), { assertive: true });
  }
}

async function openSite(url) {
  const full = /^[a-z][a-z0-9+.-]*:/i.test(url) ? url : `https://${url}`;
  if (!/^https?:/i.test(full)) {
    announce('Yalnızca http ve https adresleri açılabilir.', { assertive: true });
    return;
  }
  try {
    await openUrl(full);
    announce('Site tarayıcıda açılıyor.');
  } catch (err) {
    announce(errorText(err), { assertive: true });
  }
}

// Kısayolların hangi kayda uygulanacağı: listede odaklı kayıt ya da açık kayıt.
function targetEntry() {
  const focused = document.activeElement;
  if (focused?.closest?.('#entry-list') && focused.dataset.id) {
    return state.entries.find((e) => e.id === focused.dataset.id) ?? null;
  }
  return state.current;
}

function usernameKey(entryId) {
  const fields = state.current?.id === entryId ? state.current.fields : null;
  if (!fields || fields.some((f) => f.key === 'UserName' && f.value)) return 'UserName';
  return fields.find((f) => !f.protected && f.key !== 'Title' && f.value)?.key ?? 'UserName';
}

async function deleteCurrent() {
  const d = state.current;
  if (!d) return;
  const title = d.fields.find((f) => f.key === 'Title')?.value || 'Adsız kayıt';
  const ok = await confirmDialog('Kayıt silinsin mi?', `“${title}” kaydı kalıcı olarak silinecek. Bu işlem geri alınamaz.`, 'Sil');
  if (!ok) { $('btn-delete').focus(); return; }
  try {
    await call('delete_entry', { id: d.id });
    closeDetail(false);
    await refreshEntries();
    $('search').focus();
    announce(`“${title}” silindi. ${countText()}`);
  } catch (err) {
    announce(errorText(err), { assertive: true });
  }
}

$('btn-edit').addEventListener('click', () => state.current && startEdit(state.current.id));
$('btn-delete').addEventListener('click', deleteCurrent);
$('btn-back').addEventListener('click', () => closeDetail(true));
$('btn-new').addEventListener('click', () => startEdit(null));
$('btn-lock').addEventListener('click', () => lock());
$('btn-generator').addEventListener('click', () => openGenerator(null));
$('btn-help').addEventListener('click', () => openModal('dlg-help', 'help-close'));
$('btn-settings').addEventListener('click', openSettings);

// ---------------- Kayıt ekleme / düzenleme ----------------

let fieldCounter = 0;

async function startEdit(id) {
  let kind = DEFAULT_KIND;
  const values = {};
  const protectedKeys = new Set();
  if (id) {
    const d = state.current?.id === id ? state.current : await call('get_entry', { id });
    kind = kindOf(d.kind);
    for (const f of d.fields) {
      if (f.protected) protectedKeys.add(f.key);
      values[f.key] = f.protected ? (f.hasValue ? await call('reveal_field', { id, key: f.key }) : '') : (f.value ?? '');
    }
  }
  state.editing = { id, kind, dirty: false, protectedKeys };
  $('edit-kind-field').hidden = Boolean(id);
  $('edit-kind').value = kind;
  buildForm(kind, values);
  $('edit-title').textContent = id ? `Kaydı düzenle: ${values.Title || 'Adsız kayıt'}` : 'Yeni kayıt';
  const firstInput = $('edit-fields').querySelector('input, textarea');
  showScreen('screen-edit', id ? firstInput : $('edit-kind'), id ? 'Kaydı düzenle' : 'Yeni kayıt');
  announce(id ? 'Düzenleme formu açıldı.' : 'Yeni kayıt formu. Önce kayıt türünü seçin.');
}

function buildForm(kind, values) {
  const defs = [...KINDS[kind].fields];
  // KeePass'te başka bir uygulamayla eklenmiş, şablonda olmayan alanlar da gösterilir.
  for (const key of Object.keys(values)) {
    if (!defs.some((d) => d.key === key)) {
      defs.push({ key, label: fieldLabel(kind, key), protected: state.editing.protectedKeys.has(key) });
    }
  }
  $('edit-fields').replaceChildren(...defs.map((def) => fieldInput(def, values[def.key] ?? '')));
}

function fieldInput(def, value) {
  const id = `field-${++fieldCounter}`;
  const hintId = def.hint ? `${id}-hint` : null;
  const errorId = def.required ? `${id}-error` : null;
  const describedBy = [hintId, errorId].filter(Boolean).join(' ') || null;
  const common = {
    id,
    dataset: { key: def.key, protected: def.protected ? '1' : '0' },
    'aria-describedby': describedBy,
    'aria-required': def.required ? 'true' : null,
    autocomplete: 'off',
    spellcheck: 'false',
    inputmode: def.inputmode,
    oninput: () => { state.editing.dirty = true; },
  };
  const control = def.multiline
    ? el('textarea', common)
    : el('input', { ...common, type: def.protected ? 'password' : 'text' });
  control.value = value;

  const wrap = el('div', { className: 'form-field' },
    el('label', { for: id, text: def.required ? `${def.label} (zorunlu)` : def.label }));

  if (def.protected && !def.multiline) {
    const toggle = el('button', {
      type: 'button', className: 'small', text: 'Göster', 'aria-pressed': 'false', 'aria-label': `Göster: ${def.label}`,
      onclick: () => {
        const show = control.type === 'password';
        control.type = show ? 'text' : 'password';
        toggle.setAttribute('aria-pressed', String(show));
      },
    });
    const row = el('div', { className: 'input-row' }, control, toggle);
    if (def.generator) {
      row.append(el('button', {
        type: 'button', className: 'small', text: 'Şifre üret', 'aria-label': `Şifre üret: ${def.label}`,
        onclick: () => openGenerator(control),
      }));
    }
    wrap.append(row);
  } else {
    wrap.append(control);
  }
  if (hintId) wrap.append(el('p', { id: hintId, className: 'hint', text: def.hint }));
  if (errorId) wrap.append(el('p', { id: errorId, className: 'error', hidden: true }));
  return wrap;
}

function collectValues() {
  const values = {};
  for (const c of $('edit-fields').querySelectorAll('[data-key]')) values[c.dataset.key] = c.value;
  return values;
}

$('edit-kind').addEventListener('change', () => {
  const kind = $('edit-kind').value;
  state.editing.kind = kind;
  // Yalnızca doldurulmuş alanlar yeni türe taşınır; boş kalanlar formu kalabalıklaştırmasın.
  const filled = Object.fromEntries(Object.entries(collectValues()).filter(([, v]) => v !== ''));
  buildForm(kind, filled);
  announce(`Tür değişti: ${kindLabel(kind)}. Alanlar güncellendi.`);
});

$('edit-form').addEventListener('submit', async (e) => {
  e.preventDefault();
  const controls = [...$('edit-fields').querySelectorAll('[data-key]')];
  const titleInput = controls.find((c) => c.dataset.key === 'Title');
  const titleError = $(`${titleInput.id}-error`);
  if (!titleInput.value.trim()) {
    titleInput.setAttribute('aria-invalid', 'true');
    titleError.textContent = 'Kayıt adı boş bırakılamaz.';
    titleError.hidden = false;
    announce('Kayıt adı boş bırakılamaz.', { assertive: true });
    titleInput.focus();
    return;
  }
  titleInput.removeAttribute('aria-invalid');
  titleError.hidden = true;

  const entry = {
    id: state.editing.id,
    kind: state.editing.kind,
    fields: controls.map((c) => ({ key: c.dataset.key, value: c.value, protected: c.dataset.protected === '1' })),
  };
  try {
    const id = await call('save_entry', { entry });
    for (const c of controls) c.value = '';
    state.editing = null;
    $('edit-fields').replaceChildren();
    await refreshEntries();
    showScreen('screen-vault', false, 'Kasa');
    await openDetail(id);
    announce('Kayıt kaydedildi.');
  } catch (err) {
    announce(`Kaydedilemedi: ${errorText(err)}`, { assertive: true });
  }
});

async function cancelEdit() {
  if (state.editing?.dirty) {
    const ok = await confirmDialog('Değişiklikler kaydedilmedi', 'Yaptığınız değişiklikler kaybolacak. Çıkmak istiyor musunuz?', 'Kaydetmeden çık');
    if (!ok) return;
  }
  const id = state.editing?.id;
  state.editing = null;
  $('edit-fields').replaceChildren();
  showScreen('screen-vault', false, 'Kasa');
  if (id) await openDetail(id);
  else if (state.current) $('detail-title').focus();
  else $('search').focus();
  announce('Vazgeçildi.');
}

$('btn-edit-cancel').addEventListener('click', cancelEdit);

// ---------------- Şifre üretici ----------------

let generatorTarget = null;

function generatorOptions() {
  return {
    length: Number($('gen-length').value),
    upper: $('gen-upper').checked,
    lower: $('gen-lower').checked,
    digits: $('gen-digits').checked,
    symbols: $('gen-symbols').checked,
    avoidAmbiguous: $('gen-ambiguous').checked,
  };
}

function regenerate(speak) {
  const { password, bits } = generatePassword(generatorOptions());
  $('gen-output').value = password;
  if (!password) {
    $('gen-strength').textContent = 'En az bir karakter türü seçin.';
    if (speak) announce('En az bir karakter türü seçin.', { assertive: true });
    return;
  }
  $('gen-strength').textContent = `Güç: ${strengthText(bits)}`;
  if (!$('gen-spell-region').hidden) renderSpell($('gen-spell-list'), password, { phonetic: settings.phonetic });
  if (speak) announce(`Yeni şifre üretildi. ${Array.from(password).length} karakter, ${strengthText(bits)}.`);
}

function openGenerator(target) {
  generatorTarget = target;
  $('gen-use').hidden = !target;
  $('gen-spell-region').hidden = true;
  regenerate(false);
  $('dlg-generator').showModal();
  $('gen-output').focus();
}

for (const id of ['gen-upper', 'gen-lower', 'gen-digits', 'gen-symbols', 'gen-ambiguous']) {
  $(id).addEventListener('change', () => regenerate(true));
}
$('gen-length').addEventListener('change', () => regenerate(true));
$('gen-regenerate').addEventListener('click', () => regenerate(true));
$('gen-copy').addEventListener('click', async () => {
  const text = $('gen-output').value;
  if (!text) return;
  await call('copy_text', { text, seconds: settings.clipboardSeconds });
  announce(`Şifre kopyalandı. ${settings.clipboardSeconds} saniye sonra panodan silinecek.`);
});
$('gen-spell').addEventListener('click', () => {
  const text = $('gen-output').value;
  if (!text) return;
  $('gen-spell-heading').textContent = `Harf harf: üretilen şifre, ${Array.from(text).length} karakter`;
  renderSpell($('gen-spell-list'), text, { phonetic: settings.phonetic });
  $('gen-spell-region').hidden = false;
  $('gen-spell-heading').focus();
});
$('gen-use').addEventListener('click', () => {
  if (!generatorTarget || !$('gen-output').value) return;
  generatorTarget.value = $('gen-output').value;
  generatorTarget.dispatchEvent(new Event('input', { bubbles: true }));
  const target = generatorTarget;
  $('dlg-generator').close();
  target.focus();
  announce('Üretilen şifre forma yerleştirildi.');
});
$('gen-close').addEventListener('click', () => $('dlg-generator').close());
$('dlg-generator').addEventListener('close', () => {
  $('gen-output').value = '';
  $('gen-spell-list').replaceChildren();
  generatorTarget = null;
});

// ---------------- Yardım ve ayarlar ----------------

function openModal(dialogId, focusId) {
  $(dialogId).showModal();
  $(focusId ?? dialogId).focus();
}
$('help-close').addEventListener('click', () => $('dlg-help').close());

function openSettings() {
  $('set-autolock').value = String(settings.autoLockMinutes);
  $('set-clipboard').value = String(settings.clipboardSeconds);
  $('set-phonetic').checked = settings.phonetic;
  openModal('dlg-settings', 'set-autolock');
}
$('set-autolock').addEventListener('change', (e) => { settings.autoLockMinutes = Number(e.target.value); saveSettings(); startAutoLock(); });
$('set-clipboard').addEventListener('change', (e) => { settings.clipboardSeconds = Number(e.target.value); saveSettings(); });
$('set-phonetic').addEventListener('change', (e) => { settings.phonetic = e.target.checked; saveSettings(); });
$('settings-close').addEventListener('click', () => { $('dlg-settings').close(); announce('Ayarlar kaydedildi.'); });

// ---------------- Kilitleme ----------------

let lockTimer = null;
let warnTimer = null;
let lastActivity = 0;

function startAutoLock() {
  clearTimeout(lockTimer);
  clearTimeout(warnTimer);
  if (!state.unlocked || !settings.autoLockMinutes) return;
  const ms = settings.autoLockMinutes * 60_000;
  warnTimer = setTimeout(() => {
    announce('Kasa 30 saniye sonra kilitlenecek. Açık tutmak için herhangi bir tuşa basın.', { assertive: true });
  }, ms - 30_000);
  lockTimer = setTimeout(() => lock('Uzun süre işlem yapılmadığı için kasa kilitlendi.'), ms);
}

for (const ev of ['keydown', 'pointerdown', 'wheel', 'touchstart']) {
  document.addEventListener(ev, () => {
    const now = Date.now();
    if (state.unlocked && now - lastActivity > 1000) { lastActivity = now; startAutoLock(); }
  }, { capture: true, passive: true });
}

async function lock(message = 'Kasa kilitlendi.') {
  clearTimeout(lockTimer);
  clearTimeout(warnTimer);
  for (const d of document.querySelectorAll('dialog[open]')) d.close();
  await call('lock_vault').catch(() => {});
  state.unlocked = false;
  state.entries = [];
  state.visible = [];
  state.editing = null;
  closeDetail(false);
  $('entry-list').replaceChildren();
  $('edit-fields').replaceChildren();
  $('search').value = '';
  await showUnlock(message);
}

// ---------------- Klavye kısayolları ----------------

function isTextInput(node) {
  return node?.matches?.('input:not([type=checkbox]):not([type=radio]), textarea, select') ?? false;
}

document.addEventListener('keydown', async (e) => {
  if (document.querySelector('dialog[open]')) return; // açık pencereler kendi tuşlarını yönetir
  const ctrl = e.ctrlKey || e.metaKey;
  const key = e.key.toLowerCase();
  const inVault = !$('screen-vault').hidden;
  const inEdit = !$('screen-edit').hidden;

  if (e.key === 'F1') {
    e.preventDefault();
    if (state.unlocked) openModal('dlg-help', 'help-close');
    return;
  }

  if (inVault) {
    const target = targetEntry();
    if (ctrl && key === 'f') { e.preventDefault(); $('search').focus(); $('search').select(); }
    else if (ctrl && key === 'n') { e.preventDefault(); startEdit(null); }
    else if (ctrl && key === 'l') { e.preventDefault(); lock(); }
    else if (ctrl && key === 'g') { e.preventDefault(); openGenerator(null); }
    else if (ctrl && key === 'e' && target) { e.preventDefault(); startEdit(target.id); }
    else if (ctrl && key === 'b' && target) { e.preventDefault(); copyField(target.id, target.kind, usernameKey(target.id)); }
    else if (ctrl && key === 'c' && target && !isTextInput(e.target) && !window.getSelection()?.toString()) {
      e.preventDefault();
      copyField(target.id, target.kind, primaryKey(target.kind));
    } else if (ctrl && key === 'u' && target) {
      e.preventDefault();
      const d = state.current?.id === target.id ? state.current : await call('get_entry', { id: target.id });
      const url = d.fields.find((f) => f.key === 'URL')?.value;
      if (url) openSite(url); else announce('Bu kayıtta web adresi yok.');
    } else if (e.key === 'Delete' && state.current && !isTextInput(e.target)) { e.preventDefault(); deleteCurrent(); }
    else if (e.key === 'Escape') {
      if (e.target === $('search') && $('search').value) return; // arama kutusu kendini temizler
      if (!$('spell-region').hidden) { e.preventDefault(); hideSpell(); }
      else if (state.current) { e.preventDefault(); closeDetail(true); }
    }
  } else if (inEdit) {
    if (ctrl && key === 's') { e.preventDefault(); $('edit-form').requestSubmit(); }
    else if (ctrl && key === 'g') {
      e.preventDefault();
      const focusedField = e.target?.dataset?.protected === '1' ? e.target : null;
      openGenerator(focusedField || $('edit-fields').querySelector('[data-protected="1"]'));
    } else if (e.key === 'Escape') { e.preventDefault(); cancelEdit(); }
  }
});

// ---------------- Başlangıç ----------------

$('filter-kind').replaceChildren(
  el('option', { value: '', text: 'Tüm türler' }),
  ...Object.entries(KINDS).map(([k, v]) => el('option', { value: k, text: v.label })),
);
$('edit-kind').replaceChildren(...Object.entries(KINDS).map(([k, v]) => el('option', { value: k, text: v.label })));

onEvent('clipboard-cleared', () => announce('Pano temizlendi.'));

showUnlock();
