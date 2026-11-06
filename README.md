# KeyDone

Ekran okuyucu kullanıcıları için baştan tasarlanmış, tamamen erişilebilir bir Windows şifre ve önemli bilgi kasası.

- Gerçek bir Windows uygulamasıdır (WPF). Menü çubuğu, listeler, düğmeler ve iletişim kutuları standart Windows denetimleridir; NVDA, JAWS ve Windows Ekran Okuyucusu bunları doğrudan tanır.
- Kasa, standart **KeePass (KDBX 4)** dosyasıdır. Aynı dosya telefonda KeePassDX (Android) ya da Strongbox (iPhone) ile açılabilir.
- Her şey bilgisayarda kalır; uygulama internete hiçbir veri göndermez.
- Şifreleme: Argon2id (64 MiB) ile anahtar türetme, AES-256 ile kasa şifreleme.

## Çalıştırma

`KeyDone.bat` dosyasına çift tıklayın. İlk açılış bir iki dakika sürebilir, sonrakiler birkaç saniye sürer.
Gereken tek şey [.NET 10 SDK](https://dotnet.microsoft.com/download)'dır.

## Saklanabilen bilgiler

Giriş bilgisi, banka kartı, banka hesabı, kimlik belgesi, Wi-Fi ağı, yazılım lisansı, kripto cüzdanı ve güvenli not.
Başka KeePass uygulamalarıyla eklenmiş özel alanlar da gösterilir ve korunur.

## Kullanım

Ana pencerede solda **kayıt listesi**, sağda seçili kaydın **alanları** vardır.

- Kayıt listesinde oklarla gezin. **Enter** kaydın alanlarına geçer, alanlarda **Escape** listeye döner.
- Alan listesinde **Enter** seçili alanı kopyalar. Gizli alanlar "Şifre: gizli" diye okunur; **Göster** düğmesi değeri gösterir ve okur.
- **Harf harf oku** şifreyi "küçük a, büyük B, rakam 1, ünlem" biçiminde bir listede gösterir; oklarla karakter karakter gezilir. Ayarlardan "A, Adana" gibi kod kelimeleri de açılabilir.
- Her işlem ekran okuyucuya sesli bildirilir ("Şifre kopyalandı, 30 saniye sonra panodan silinecek" gibi).

### Klavye kısayolları (F1 bu listeyi açar)

| Tuş | İşlem |
| --- | --- |
| Ctrl+F | Arama kutusuna git |
| Ctrl+N | Yeni kayıt |
| Ctrl+E | Seçili kaydı düzenle |
| Delete | Seçili kaydı sil (onay istenir) |
| Ctrl+C | Şifreyi ya da seçili alanı kopyala |
| Ctrl+B | Kullanıcı adını kopyala |
| Ctrl+H | Şifreyi ya da seçili alanı harf harf oku |
| Ctrl+U | Web sitesini aç |
| Ctrl+G | Şifre üretici |
| Ctrl+L | Kasayı kilitle |
| Ctrl+O | Başka bir kasa aç |
| Ctrl+Shift+N | Yeni kasa oluştur |
| Ctrl+S | Kayıt penceresinde kaydet |

## Güvenlik

- Kasa hareketsiz kalınca (varsayılan 5 dakika) ve Windows kilitlenince kendiliğinden kilitlenir; 30 saniye önce sesli uyarı verilir.
- Kopyalanan bilgi ayarlanan süre sonunda panodan silinir ve Windows pano geçmişine ya da buluta gönderilmez.
- Kasa önce geçici dosyaya yazılır, sonra asıl dosyanın yerine konur; yazma yarıda kesilirse eski kasa bozulmaz.
- Kayıt düzenlenince eski hali KeePass'teki gibi kaydın geçmişine eklenir.

## Geliştirme

```bash
dotnet build -c Release
dotnet test
```

- `src/KeyDone.Core`: Kasa (KeePassLib), kayıt türleri, şifre üretici, harf harf okuma.
- `src/KeyDone`: WPF arayüzü.
- `tests/KeyDone.Core.Tests`: Testler.

KDBX dosyaları, KeePass'in kendi kütüphanesi KeePassLib ile okunup yazılır (KeePassLib.Standard paketi). KeePassLib GPL-2.0 lisanslıdır; bu yüzden KeyDone dağıtılırsa kaynak kodu da GPL ile açık olmalıdır.

## Yol haritası

1. **Temel sürüm (şu an):** Kasa oluşturma ve açma, kayıt türleri, arama, kopyalama, sesli bildirimler, harf harf okuma, şifre üretici, otomatik kilit.
2. **İkinci aşama:** Dosya ekleri, Türkçe kelimelerle parola cümlesi, iki adımlı doğrulama (TOTP) kodları, zayıf ve tekrar kullanılan şifre denetimi, içe aktarma (Bitwarden, Chrome CSV).
3. **Üçüncü aşama:** Başka uygulamalara otomatik yazma, Windows Hello ile açma, isteğe bağlı sesli işaretler, Braille yazıcı için acil durum kiti, Android uygulaması.
