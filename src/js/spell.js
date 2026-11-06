// Bir metni karakter karakter, ekran okuyucunun karıştırmayacağı biçimde söyler.
// Örnek: "aB1!" -> küçük a, büyük B, rakam 1, ünlem

const PHONETIC = {
  A: 'Adana', B: 'Bolu', C: 'Ceyhan', 'Ç': 'Çanakkale', D: 'Denizli', E: 'Edirne',
  F: 'Fatsa', G: 'Giresun', 'Ğ': 'yumuşak ge', H: 'Hatay', I: 'Isparta', 'İ': 'İzmir',
  J: 'Jandarma', K: 'Kars', L: 'Lüleburgaz', M: 'Muş', N: 'Niğde', O: 'Ordu',
  'Ö': 'Ödemiş', P: 'Pazar', Q: 'ku', R: 'Rize', S: 'Sinop', 'Ş': 'Şırnak',
  T: 'Tokat', U: 'Uşak', 'Ü': 'Ünye', V: 'Van', W: 'duble ve', X: 'iks',
  Y: 'Yozgat', Z: 'Zonguldak',
};

const SYMBOLS = {
  ' ': 'boşluk', '!': 'ünlem', '"': 'çift tırnak', '#': 'diyez', '$': 'dolar',
  '%': 'yüzde', '&': 've işareti', "'": 'tek tırnak', '(': 'parantez aç', ')': 'parantez kapat',
  '*': 'yıldız', '+': 'artı', ',': 'virgül', '-': 'tire', '.': 'nokta', '/': 'eğik çizgi',
  ':': 'iki nokta', ';': 'noktalı virgül', '<': 'küçüktür', '=': 'eşittir', '>': 'büyüktür',
  '?': 'soru işareti', '@': 'et işareti', '[': 'köşeli parantez aç', '\\': 'ters eğik çizgi',
  ']': 'köşeli parantez kapat', '^': 'şapka', '_': 'alt çizgi', '`': 'ters tırnak',
  '{': 'süslü parantez aç', '|': 'dikey çizgi', '}': 'süslü parantez kapat', '~': 'dalga işareti',
  '€': 'avro', '£': 'sterlin', '₺': 'Türk lirası', '\t': 'sekme', '\n': 'yeni satır',
};

function describeChar(ch, phonetic) {
  if (/\p{L}/u.test(ch)) {
    const upper = ch.toLocaleUpperCase('tr');
    const isUpper = ch === upper && ch !== ch.toLocaleLowerCase('tr');
    let text = `${isUpper ? 'büyük' : 'küçük'} ${ch}`;
    if (phonetic && PHONETIC[upper]) text += `, ${PHONETIC[upper]}`;
    return text;
  }
  if (/\p{Nd}/u.test(ch)) return `rakam ${ch}`;
  if (SYMBOLS[ch]) return SYMBOLS[ch];
  return `işaret ${ch}`;
}

// Her karakter için okunacak metni döndürür.
export function spell(text, { phonetic = false } = {}) {
  return Array.from(text).map((ch) => describeChar(ch, phonetic));
}

// Sonucu bir <ol> listesine yazar.
export function renderSpell(listEl, text, options) {
  listEl.replaceChildren(...spell(text, options).map((t) => {
    const li = document.createElement('li');
    li.textContent = t;
    return li;
  }));
}
