// Güvenli rastgele şifre üretici (tarayıcının kriptografik rastgele sayı üreticisini kullanır).

const SETS = {
  upper: 'ABCDEFGHIJKLMNOPQRSTUVWXYZ',
  lower: 'abcdefghijklmnopqrstuvwxyz',
  digits: '0123456789',
  symbols: '!@#$%&*()-_=+[]{};:,.?/',
};
const AMBIGUOUS = new Set('Il1O0o|'.split(''));

// 0 ile max-1 arasında eşit dağılımlı rastgele tam sayı.
function randomInt(max) {
  const limit = Math.floor(0x100000000 / max) * max;
  const buf = new Uint32Array(1);
  do { crypto.getRandomValues(buf); } while (buf[0] >= limit);
  return buf[0] % max;
}

export function generatePassword(opts) {
  const length = Math.min(128, Math.max(4, Number(opts.length) || 20));
  const pools = Object.keys(SETS)
    .filter((k) => opts[k])
    .map((k) => SETS[k].split('').filter((c) => !(opts.avoidAmbiguous && AMBIGUOUS.has(c))));
  if (pools.length === 0) return { password: '', bits: 0 };

  const all = pools.flat();
  const chars = pools.map((p) => p[randomInt(p.length)]); // her seçili gruptan en az bir karakter
  while (chars.length < length) chars.push(all[randomInt(all.length)]);
  for (let i = chars.length - 1; i > 0; i--) {
    const j = randomInt(i + 1);
    [chars[i], chars[j]] = [chars[j], chars[i]];
  }
  return { password: chars.join(''), bits: Math.round(length * Math.log2(all.length)) };
}

// Yazılmış bir şifrenin gücünü kabaca tahmin eder (karakter çeşitliliği ve uzunluğa göre).
export function estimateBits(text) {
  if (!text) return 0;
  let pool = 0;
  if (/[a-zçğıöşü]/.test(text)) pool += 26;
  if (/[A-ZÇĞİÖŞÜ]/.test(text)) pool += 26;
  if (/\d/.test(text)) pool += 10;
  if (/[^A-Za-z0-9çğıöşüÇĞİÖŞÜ]/.test(text)) pool += 20;
  const unique = new Set(text).size;
  const effectiveLength = Math.min(Array.from(text).length, unique * 2);
  return Math.round(effectiveLength * Math.log2(Math.max(pool, 2)));
}

export function strengthText(bits) {
  let level;
  if (bits < 40) level = 'Zayıf';
  else if (bits < 60) level = 'Orta';
  else if (bits < 80) level = 'Güçlü';
  else level = 'Çok güçlü';
  return `${level} (yaklaşık ${bits} bit)`;
}
