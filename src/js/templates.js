// Kayıt türleri ve alanları.
// Title, UserName, Password, URL ve Notes, KeePass'in standart alan adlarıdır;
// diğer alanlar KeePass dosyasında Türkçe adlarıyla özel alan olarak saklanır.
// Böylece kasa KeePassXC, KeePassDX ve Strongbox ile de okunabilir.

const TITLE = { key: 'Title', label: 'Kayıt adı', required: true };
const NOTES = { key: 'Notes', label: 'Notlar', multiline: true };

export const KINDS = {
  giris: {
    label: 'Giriş bilgisi',
    fields: [
      TITLE,
      { key: 'UserName', label: 'Kullanıcı adı veya e-posta', autocomplete: 'off' },
      { key: 'Password', label: 'Şifre', protected: true, generator: true, primary: true },
      { key: 'URL', label: 'Web adresi', inputmode: 'url', hint: 'Örnek: https://www.ornek.com' },
      NOTES,
    ],
  },
  kart: {
    label: 'Banka kartı',
    fields: [
      TITLE,
      { key: 'Kart sahibi', label: 'Kart sahibi' },
      { key: 'Kart numarası', label: 'Kart numarası', protected: true, inputmode: 'numeric', primary: true },
      { key: 'Son kullanma tarihi', label: 'Son kullanma tarihi', hint: 'Ay ve yıl, örnek: 05/2029' },
      { key: 'Güvenlik kodu', label: 'Güvenlik kodu (CVV)', protected: true, inputmode: 'numeric' },
      { key: 'PIN', label: 'PIN', protected: true, inputmode: 'numeric' },
      { key: 'Banka', label: 'Banka' },
      NOTES,
    ],
  },
  hesap: {
    label: 'Banka hesabı',
    fields: [
      TITLE,
      { key: 'Banka', label: 'Banka' },
      { key: 'Hesap sahibi', label: 'Hesap sahibi' },
      { key: 'IBAN', label: 'IBAN', hint: 'TR ile başlayan 26 karakter' },
      { key: 'Hesap numarası', label: 'Hesap numarası' },
      { key: 'Şube', label: 'Şube' },
      { key: 'UserName', label: 'Müşteri numarası veya internet bankacılığı kullanıcı adı' },
      { key: 'Password', label: 'İnternet bankacılığı şifresi', protected: true, primary: true },
      NOTES,
    ],
  },
  kimlik: {
    label: 'Kimlik belgesi',
    fields: [
      TITLE,
      { key: 'Belge türü', label: 'Belge türü', hint: 'Örnek: Kimlik kartı, pasaport, ehliyet' },
      { key: 'Ad soyad', label: 'Ad soyad' },
      { key: 'T.C. kimlik numarası', label: 'T.C. kimlik numarası', protected: true, inputmode: 'numeric', primary: true },
      { key: 'Belge numarası', label: 'Belge numarası', protected: true },
      { key: 'Veriliş tarihi', label: 'Veriliş tarihi', hint: 'Örnek: 14.03.2022' },
      { key: 'Geçerlilik tarihi', label: 'Geçerlilik tarihi', hint: 'Örnek: 14.03.2032' },
      NOTES,
    ],
  },
  wifi: {
    label: 'Wi-Fi ağı',
    fields: [
      TITLE,
      { key: 'Ağ adı', label: 'Ağ adı (SSID)' },
      { key: 'Password', label: 'Wi-Fi şifresi', protected: true, generator: true, primary: true },
      { key: 'Güvenlik türü', label: 'Güvenlik türü', hint: 'Örnek: WPA2, WPA3' },
      NOTES,
    ],
  },
  lisans: {
    label: 'Yazılım lisansı',
    fields: [
      TITLE,
      { key: 'Yazılım', label: 'Yazılım' },
      { key: 'Lisans anahtarı', label: 'Lisans anahtarı', protected: true, primary: true },
      { key: 'UserName', label: 'Kayıtlı e-posta' },
      { key: 'Satın alma tarihi', label: 'Satın alma tarihi' },
      NOTES,
    ],
  },
  kripto: {
    label: 'Kripto cüzdanı',
    fields: [
      TITLE,
      { key: 'Kurtarma kelimeleri', label: 'Kurtarma kelimeleri', protected: true, multiline: true, primary: true },
      { key: 'Cüzdan adresi', label: 'Cüzdan adresi' },
      { key: 'Password', label: 'Cüzdan şifresi', protected: true },
      NOTES,
    ],
  },
  not: {
    label: 'Güvenli not',
    fields: [
      TITLE,
      { key: 'Gizli not', label: 'Not', protected: true, multiline: true, primary: true },
    ],
  },
};

export const DEFAULT_KIND = 'giris';

const STANDARD_LABELS = {
  Title: 'Kayıt adı', UserName: 'Kullanıcı adı', Password: 'Şifre', URL: 'Web adresi', Notes: 'Notlar',
};

export function kindOf(kind) {
  return KINDS[kind] ? kind : DEFAULT_KIND;
}

export function kindLabel(kind) {
  return KINDS[kindOf(kind)].label;
}

export function fieldDef(kind, key) {
  return KINDS[kindOf(kind)].fields.find((f) => f.key === key);
}

export function fieldLabel(kind, key) {
  return fieldDef(kind, key)?.label ?? STANDARD_LABELS[key] ?? key;
}

// Ctrl+C ile kopyalanacak ana gizli alan.
export function primaryKey(kind) {
  return KINDS[kindOf(kind)].fields.find((f) => f.primary)?.key ?? 'Password';
}
