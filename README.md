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

### Gezinme

- **Tab** ana pencerede yalnızca dört yerde durur: arama kutusu, kategoriler, kayıt listesi, alan listesi. Ana pencerede düğme yoktur ve Tab ile menü çubuğuna gidilmez.
- **Kategoriler** bir sekme şerididir: Tümü, Giriş bilgisi, Banka kartı... Her sekme kayıt sayısını da söyler ("Banka kartı (2)"). Kategorilerdeyken **sağ ve sol ok** kategori değiştirir; **Ctrl+Tab** ve **Ctrl+Shift+Tab** her yerden sonraki ve önceki kategoriye geçer.
- **Ok tuşları** yalnızca listelerin ve kategorilerin içinde çalışır; hiçbir pencerede düğmeler arasında gezdirmez.
- **F6** kayıt listesi ile alan listesi arasında geçer. Ayarlar penceresinde Ctrl+Tab sekmeler arasında geçer.
- **Alt** ya da **F10** menü çubuğunu açar. Tüm işlemler menülerdedir: Dosya, Kayıt, Görünüm, Araçlar, Yardım.
- **Uygulama tuşu** ya da **Shift+F10** seçili kayıt ya da alan için işlemler menüsünü açar.

### Listeler

- Kayıt listesinin sütunları: Kayıt adı, Tür, Kullanıcı adı. Ekran okuyucu satırı "E-posta, Giriş bilgisi, ornek@posta.com" diye okur.
- Kayıt listesinde **Enter** alanlara geçer. Alan listesinde **Enter** seçili alanı kopyalar, **Escape** kayıt listesine döner.
- Gizli alanlar "Şifre: gizli" diye okunur; **Ctrl+R** değeri gösterir ve okur.
- **Ctrl+H** şifreyi "küçük a, büyük B, rakam 1, ünlem" biçiminde bir listede gösterir; oklarla karakter karakter gezilir.

### Varsayılan kısayollar (F1 güncel listeyi açar)

| Tuş | İşlem |
| --- | --- |
| Ctrl+F | Arama kutusuna git |
| Ctrl+N | Yeni kayıt |
| Ctrl+E | Seçili kaydı düzenle |
| Delete | Seçili kaydı sil (onay istenir) |
| Ctrl+C | Şifreyi ya da seçili alanı kopyala |
| Ctrl+B | Kullanıcı adını kopyala |
| Ctrl+R | Seçili alanı göster ya da gizle |
| Ctrl+H | Şifreyi ya da seçili alanı harf harf oku |
| Ctrl+U | Web sitesini aç |
| Ctrl+G | Şifre üretici |
| Ctrl+L | Kasayı kilitle |
| Ctrl+O | Başka bir kasa aç |
| Ctrl+Shift+N | Yeni kasa oluştur |
| Ctrl+Shift+K | Kısayolları özelleştir |
| F9 | Ayarlar |
| Ctrl+S | Kayıt penceresinde kaydet |

**Kısayolları değiştirmek için:** Araçlar menüsünden Kısayolları özelleştir (Ctrl+Shift+K). Listede işlemi seçip Enter'a basın, yeni tuşlara basın, Enter ile onaylayın. Delete kısayolu kaldırır. Başka bir işlemde kullanılan bir kısayol seçilirse sorulur.

### Sesler

Kasa açılınca, kilitlenince, kopyalayınca, pano temizlenince, kaydedince, silince, hata olunca ve kasa kilitlenmek üzereyken kısa bir ses çalar. Ayarlar penceresinin Sesler sekmesinden kapatılabilir, ses düzeyi seçilebilir ve her ses dinlenebilir.

Bir sesi kendi sesinizle değiştirmek için `%APPDATA%\KeyDone\Sesler` klasörüne şu adlardan biriyle bir WAV dosyası koyun: `acildi.wav`, `kilitlendi.wav`, `kopyalandi.wav`, `pano-temizlendi.wav`, `kaydedildi.wav`, `silindi.wav`, `hata.wav`, `uyari.wav`.

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
