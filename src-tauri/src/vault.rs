//! KeePass (KDBX 4) kasasını açma, okuma, değiştirme ve kaydetme.
//!
//! Gizli alanlar (şifre, PIN gibi) arayüze yalnızca kullanıcı açıkça isteyince gönderilir.

use std::{
    fs::{self, File},
    path::{Path, PathBuf},
};

use keepass::{
    config::{DatabaseConfig, KdfConfig},
    db::{fields, CustomDataItem, CustomDataValue, Entry, EntryId, EntryRef, Times, Value},
    Database, DatabaseKey,
};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

/// Kayıt türünün saklandığı özel veri anahtarı. Diğer KeePass uygulamaları bunu görmezden gelir.
pub const KIND_KEY: &str = "KeyDone.Kind";
/// Değerin önüne eklenir. keepass kütüphanesi base64'e benzeyen değerleri ("kart" gibi)
/// ikili veri sanıyor; iki nokta base64'te bulunmadığı için değer her zaman metin kalır.
const KIND_PREFIX: &str = "tur:";

pub struct Vault {
    path: PathBuf,
    db: Database,
    key: DatabaseKey,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct EntrySummary {
    pub id: String,
    pub kind: String,
    pub title: String,
    pub username: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct FieldOut {
    pub key: String,
    /// Gizli alanlarda her zaman boştur; değer `reveal` ile ayrıca istenir.
    pub value: Option<String>,
    pub protected: bool,
    pub has_value: bool,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct EntryDetail {
    pub id: String,
    pub kind: String,
    pub fields: Vec<FieldOut>,
}

#[derive(Deserialize)]
pub struct FieldIn {
    pub key: String,
    pub value: String,
    pub protected: bool,
}

#[derive(Deserialize)]
pub struct EntryInput {
    pub id: Option<String>,
    pub kind: String,
    pub fields: Vec<FieldIn>,
}

/// Yeni kasalar için anahtar türetme ayarı: Argon2id, 64 MiB bellek.
/// Masaüstünde yaklaşık yarım saniye sürer; tahmin saldırılarını çok yavaşlatır.
pub fn default_kdf() -> KdfConfig {
    KdfConfig::Argon2id {
        iterations: 10,
        memory: 64 * 1024 * 1024,
        parallelism: 2,
        version: argon2::Version::Version13,
    }
}

fn open_error(err: keepass::db::DatabaseOpenError) -> String {
    use keepass::db::DatabaseOpenError as E;
    match err {
        E::Key(_) => "Ana şifre yanlış.".into(),
        E::Io(e) => format!("Kasa dosyası okunamadı: {e}"),
        E::UnsupportedVersion => "Bu kasa dosyasının sürümü desteklenmiyor.".into(),
        _ => "Kasa açılamadı. Dosya bir KeePass kasası olmayabilir ya da bozulmuş olabilir.".into(),
    }
}

fn entry_id(id: &str) -> Result<EntryId, String> {
    Uuid::parse_str(id).map(EntryId::from).map_err(|_| "Kayıt bulunamadı.".to_string())
}

fn kind_of(entry: &Entry) -> String {
    match entry.custom_data.get(KIND_KEY).and_then(|item| item.value.as_ref()) {
        Some(CustomDataValue::String(kind)) => kind.strip_prefix(KIND_PREFIX).unwrap_or(kind).to_string(),
        _ => String::new(),
    }
}

/// Formdan gelen değerleri kayda uygular.
fn apply(entry: &mut Entry, input: &EntryInput) {
    for field in &input.fields {
        let is_standard = fields::KNOWN_FIELDS.contains(&field.key.as_str());
        if field.value.is_empty() && !is_standard {
            entry.fields.remove(&field.key);
            continue;
        }
        let protected = field.protected || field.key == fields::PASSWORD;
        let value = if protected {
            Value::protected(field.value.clone())
        } else {
            Value::unprotected(field.value.clone())
        };
        entry.set(field.key.clone(), value);
    }
    entry.custom_data.insert(
        KIND_KEY.to_string(),
        CustomDataItem {
            value: Some(CustomDataValue::String(format!("{KIND_PREFIX}{}", input.kind))),
            last_modification_time: Some(Times::now()),
        },
    );
    entry.times.last_modification = Some(Times::now());
}

impl Vault {
    pub fn create(path: &Path, password: &str, kdf: KdfConfig) -> Result<Self, String> {
        let mut config = DatabaseConfig::default();
        config.kdf_config = kdf;
        let mut db = Database::with_config(config);
        db.meta.generator = Some("KeyDone".into());
        db.meta.database_name = Some("KeyDone".into());
        db.root_mut().name = "KeyDone".into();
        let vault = Vault {
            path: path.to_path_buf(),
            db,
            key: DatabaseKey::new().with_password(password),
        };
        vault.save()?;
        Ok(vault)
    }

    pub fn open(path: &Path, password: &str) -> Result<Self, String> {
        let mut file = File::open(path).map_err(|e| format!("Kasa dosyası açılamadı: {e}"))?;
        let key = DatabaseKey::new().with_password(password);
        let db = Database::open(&mut file, key.clone()).map_err(open_error)?;
        Ok(Vault { path: path.to_path_buf(), db, key })
    }

    /// Önce geçici dosyaya yazar, sonra asıl dosyanın yerine koyar.
    /// Böylece yazma yarıda kesilirse eski kasa bozulmaz.
    pub fn save(&self) -> Result<(), String> {
        let tmp = self.path.with_extension("kdbx.tmp");
        let result = (|| {
            let mut file = File::create(&tmp).map_err(|e| e.to_string())?;
            self.db.save(&mut file, self.key.clone()).map_err(|e| e.to_string())?;
            file.sync_all().map_err(|e| e.to_string())?;
            drop(file);
            fs::rename(&tmp, &self.path).map_err(|e| e.to_string())
        })();
        if result.is_err() {
            let _ = fs::remove_file(&tmp);
        }
        result.map_err(|e| format!("Kasa kaydedilemedi: {e}"))
    }

    /// Değişikliği uygular ve kaydeder; kayıt başarısız olursa bellekteki kasayı eski haline döndürür.
    fn change<T>(&mut self, f: impl FnOnce(&mut Database) -> Result<T, String>) -> Result<T, String> {
        let backup = self.db.clone();
        let result = f(&mut self.db).and_then(|value| self.save().map(|_| value));
        if result.is_err() {
            self.db = backup;
        }
        result
    }

    fn in_recycle_bin(&self, entry: &EntryRef<'_>) -> bool {
        let Some(bin_id) = self.db.recycle_bin().map(|g| g.id()) else {
            return false;
        };
        let mut group_id = entry.parent().id();
        loop {
            if group_id == bin_id {
                return true;
            }
            match self.db.group(group_id).and_then(|g| g.parent().map(|p| p.id())) {
                Some(parent) => group_id = parent,
                None => return false,
            }
        }
    }

    pub fn list(&self) -> Vec<EntrySummary> {
        self.db
            .iter_all_entries()
            .filter(|e| !self.in_recycle_bin(e))
            .map(|e| EntrySummary {
                id: e.id().uuid().to_string(),
                kind: kind_of(&e),
                title: e.get_title().unwrap_or_default().to_string(),
                username: e.get_username().unwrap_or_default().to_string(),
            })
            .collect()
    }

    pub fn detail(&self, id: &str) -> Result<EntryDetail, String> {
        let entry = self.db.entry(entry_id(id)?).ok_or("Kayıt bulunamadı.")?;
        let fields = entry
            .fields
            .iter()
            .map(|(key, value)| {
                let protected = value.is_protected();
                FieldOut {
                    key: key.clone(),
                    value: (!protected).then(|| value.get().clone()),
                    protected,
                    has_value: !value.get().is_empty(),
                }
            })
            .collect();
        Ok(EntryDetail { id: id.to_string(), kind: kind_of(&entry), fields })
    }

    pub fn reveal(&self, id: &str, key: &str) -> Result<String, String> {
        let entry = self.db.entry(entry_id(id)?).ok_or("Kayıt bulunamadı.")?;
        Ok(entry.get(key).unwrap_or_default().to_string())
    }

    pub fn save_entry(&mut self, input: &EntryInput) -> Result<String, String> {
        let has_title = input
            .fields
            .iter()
            .any(|f| f.key == fields::TITLE && !f.value.trim().is_empty());
        if !has_title {
            return Err("Kayıt adı boş bırakılamaz.".into());
        }
        let existing = input.id.as_deref().map(entry_id).transpose()?;
        self.change(|db| match existing {
            Some(id) => {
                let mut entry = db.entry_mut(id).ok_or("Kayıt bulunamadı.")?;
                // Değişiklik geçmişi KeePass'teki gibi kayda eklenir.
                let mut tracked = entry.track_changes();
                apply(&mut tracked, input);
                Ok(id.uuid().to_string())
            }
            None => {
                let mut root = db.root_mut();
                let mut entry = root.add_entry();
                apply(&mut entry, input);
                Ok(entry.id().uuid().to_string())
            }
        })
    }

    pub fn delete(&mut self, id: &str) -> Result<(), String> {
        let id = entry_id(id)?;
        self.change(|db| {
            let mut entry = db.entry_mut(id).ok_or("Kayıt bulunamadı.")?;
            entry.track_changes().remove();
            Ok(())
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Testlerin hızlı çalışması için düşük maliyetli anahtar türetme.
    fn fast_kdf() -> KdfConfig {
        KdfConfig::Argon2id { iterations: 1, memory: 1024 * 1024, parallelism: 1, version: argon2::Version::Version13 }
    }

    fn temp_path(name: &str) -> PathBuf {
        std::env::temp_dir().join(format!("keydone-test-{name}-{}.kdbx", Uuid::new_v4()))
    }

    fn field(key: &str, value: &str, protected: bool) -> FieldIn {
        FieldIn { key: key.into(), value: value.into(), protected }
    }

    #[test]
    fn create_save_reopen_roundtrip() {
        let path = temp_path("roundtrip");
        let mut vault = Vault::create(&path, "doğru şifre", fast_kdf()).unwrap();
        let id = vault
            .save_entry(&EntryInput {
                id: None,
                kind: "kart".into(),
                fields: vec![
                    field("Title", "Maaş kartı", false),
                    field("Kart numarası", "4111 1111 1111 1111", true),
                    field("Banka", "Örnek Bankası", false),
                    field("PIN", "", true),
                ],
            })
            .unwrap();

        let reopened = Vault::open(&path, "doğru şifre").unwrap();
        let list = reopened.list();
        assert_eq!(list.len(), 1);
        assert_eq!(list[0].title, "Maaş kartı");
        assert_eq!(list[0].kind, "kart");

        let detail = reopened.detail(&id).unwrap();
        let card = detail.fields.iter().find(|f| f.key == "Kart numarası").unwrap();
        assert!(card.protected);
        assert!(card.value.is_none(), "gizli alan arayüze gönderilmemeli");
        assert!(detail.fields.iter().all(|f| f.key != "PIN"), "boş özel alan saklanmamalı");
        assert_eq!(reopened.reveal(&id, "Kart numarası").unwrap(), "4111 1111 1111 1111");

        fs::remove_file(&path).unwrap();
    }

    #[test]
    fn wrong_password_is_reported() {
        let path = temp_path("wrong");
        Vault::create(&path, "doğru", fast_kdf()).unwrap();
        let err = Vault::open(&path, "yanlış").err().unwrap();
        assert_eq!(err, "Ana şifre yanlış.");
        fs::remove_file(&path).unwrap();
    }

    #[test]
    fn edit_keeps_history_and_delete_removes() {
        let path = temp_path("edit");
        let mut vault = Vault::create(&path, "x", fast_kdf()).unwrap();
        let input = |id: Option<String>, pw: &str| EntryInput {
            id,
            kind: "giris".into(),
            fields: vec![field("Title", "E-posta", false), field("Password", pw, false)],
        };
        let id = vault.save_entry(&input(None, "eski")).unwrap();
        vault.save_entry(&input(Some(id.clone()), "yeni")).unwrap();

        let reopened = Vault::open(&path, "x").unwrap();
        assert_eq!(reopened.reveal(&id, "Password").unwrap(), "yeni");
        let detail = reopened.detail(&id).unwrap();
        assert!(detail.fields.iter().find(|f| f.key == "Password").unwrap().protected, "şifre her zaman gizli saklanmalı");
        let history = reopened.db.entry(entry_id(&id).unwrap()).unwrap().history.clone().unwrap();
        assert_eq!(history.get_entries().len(), 1);

        let mut reopened = reopened;
        reopened.delete(&id).unwrap();
        assert!(Vault::open(&path, "x").unwrap().list().is_empty());
        fs::remove_file(&path).unwrap();
    }

    #[test]
    fn empty_title_is_rejected() {
        let path = temp_path("title");
        let mut vault = Vault::create(&path, "x", fast_kdf()).unwrap();
        let err = vault
            .save_entry(&EntryInput { id: None, kind: "not".into(), fields: vec![field("Title", "  ", false)] })
            .unwrap_err();
        assert_eq!(err, "Kayıt adı boş bırakılamaz.");
        assert!(vault.list().is_empty());
        fs::remove_file(&path).unwrap();
    }
}
