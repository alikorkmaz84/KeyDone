# KeyDone

Ekran okuyucu kullanıcıları için baştan tasarlanmış, tamamen erişilebilir bir şifre ve önemli bilgi kasası.

- Kasa, standart **KeePass (KDBX 4)** dosyasıdır. Aynı dosya KeePassXC, KeePassDX (Android) ve Strongbox (iPhone) ile de açılabilir.
- Her şey cihazda kalır; uygulama internete hiçbir veri göndermez.
- Şifreleme: Argon2id (64 MiB) ile anahtar türetme, AES-256 ile kasa şifreleme.

## Saklanabilen bilgiler

Giriş bilgisi, banka kartı, banka hesabı, kimlik belgesi, Wi-Fi ağı, yazılım lisansı, kripto cüzdanı ve güvenli not.
Başka KeePass uygulamalarıyla eklenmiş özel alanlar da gösterilir ve korunur.

## Erişilebilirlik

- Fare gerekmez; tüm işlemler klavyeyle yapılır (F1 kısayol listesini açar).
- Her işlem ekran okuyucuya sesli bildirilir (kopyalandı, kaydedildi, pano temizlendi...).
- **Harf harf okuma:** Şifreler "büyük A, küçük l, rakam 1, ünlem" biçiminde, istenirse "A, Adana" gibi kod kelimeleriyle okunur.
- Şifre gücü renkle değil sözle bildirilir.
- Kendiliğinden kilitlenmeden 30 saniye önce uyarı verilir; süre ayarlanabilir.
- Yüksek karşıtlık modu, karanlık tema ve büyük yazı desteklenir.

## Güvenlik

- Gizli alanlar (şifre, PIN, kart numarası) arayüze yalnızca kullanıcı "Göster", "Harf harf oku" ya da "Düzenle" dediğinde gönderilir.
- Kopyalama Rust tarafında yapılır; pano ayarlanan süre sonunda temizlenir. Windows'ta kopyalanan bilgi pano geçmişine ve buluta düşmez.
- Kasa önce geçici dosyaya yazılır, sonra asıl dosyanın yerine konur; yazma yarıda kesilirse eski kasa bozulmaz.
- Kayıt düzenlenince eski hali KeePass'teki gibi kaydın geçmişine eklenir.

## Geliştirme

Gerekenler: Node.js, Rust, Windows'ta Visual Studio C++ Derleme Araçları ([Tauri ön koşulları](https://tauri.app/start/prerequisites/)).

```bash
npm install
npm run tauri dev
```

Rust testleri:

```bash
cd src-tauri
cargo test
```

`src/index.html` dosyası Tauri dışında, sıradan bir tarayıcıda da açılabilir. Bu durumda arayüzü denemek için bellekte çalışan sahte bir arka uç kullanılır (ana şifre: `deneme`); hiçbir şey kaydedilmez.

### Android

Android Studio, Android SDK ve NDK kurulduktan sonra:

```bash
npm run tauri android init
npm run tauri android dev
```

## Yol haritası

1. **Temel sürüm (şu an):** Kasa oluşturma ve açma, kayıt türleri, arama, kopyalama, sesli bildirimler, harf harf okuma, şifre üretici, otomatik kilit.
2. **İkinci aşama:** Dosya ekleri, Türkçe kelimelerle parola cümlesi üretme, iki adımlı doğrulama (TOTP) kodları, zayıf ve tekrar kullanılan şifre denetimi, içe aktarma (Bitwarden, Chrome CSV), Android'de dosya seçme.
3. **Üçüncü aşama:** Başka uygulamalara otomatik yazma, Windows Hello ile açma, isteğe bağlı sesli işaretler, Braille yazıcı için acil durum kiti.
