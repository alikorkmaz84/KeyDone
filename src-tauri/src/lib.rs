mod clipboard;
mod vault;

use std::{
    path::Path,
    sync::{Arc, Mutex},
};

use tauri::{AppHandle, Manager, State};
use vault::{EntryDetail, EntryInput, EntrySummary, Vault};

type Shared = Arc<Mutex<Option<Vault>>>;

#[derive(Default)]
struct AppState {
    vault: Shared,
}

fn with_vault<T>(shared: &Shared, f: impl FnOnce(&mut Vault) -> Result<T, String>) -> Result<T, String> {
    let mut guard = shared.lock().map_err(|_| "İç hata: kasa kilidi bozuldu.".to_string())?;
    let vault = guard.as_mut().ok_or("Kasa kilitli.")?;
    f(vault)
}

/// Uzun süren işleri (anahtar türetme, kaydetme) arayüzü dondurmadan arka planda çalıştırır.
async fn blocking<T: Send + 'static>(f: impl FnOnce() -> Result<T, String> + Send + 'static) -> Result<T, String> {
    tauri::async_runtime::spawn_blocking(f)
        .await
        .map_err(|e| format!("İç hata: {e}"))?
}

#[tauri::command]
fn default_vault_path(app: AppHandle) -> Result<String, String> {
    let dir = app.path().app_data_dir().map_err(|e| e.to_string())?;
    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    Ok(dir.join("KeyDone.kdbx").to_string_lossy().into_owned())
}

#[tauri::command]
fn vault_exists(path: String) -> bool {
    Path::new(&path).is_file()
}

#[tauri::command]
async fn create_vault(state: State<'_, AppState>, path: String, password: String) -> Result<(), String> {
    let vault = blocking(move || Vault::create(Path::new(&path), &password, vault::default_kdf())).await?;
    *state.vault.lock().map_err(|_| "İç hata.")? = Some(vault);
    Ok(())
}

#[tauri::command]
async fn open_vault(state: State<'_, AppState>, path: String, password: String) -> Result<(), String> {
    let vault = blocking(move || Vault::open(Path::new(&path), &password)).await?;
    *state.vault.lock().map_err(|_| "İç hata.")? = Some(vault);
    Ok(())
}

#[tauri::command]
fn lock_vault(state: State<'_, AppState>) -> Result<(), String> {
    // Kasa bellekten silinir; gizli değerler silinirken sıfırlanır.
    *state.vault.lock().map_err(|_| "İç hata.")? = None;
    Ok(())
}

#[tauri::command]
fn list_entries(state: State<'_, AppState>) -> Result<Vec<EntrySummary>, String> {
    with_vault(&state.vault, |v| Ok(v.list()))
}

#[tauri::command]
fn get_entry(state: State<'_, AppState>, id: String) -> Result<EntryDetail, String> {
    with_vault(&state.vault, |v| v.detail(&id))
}

#[tauri::command]
fn reveal_field(state: State<'_, AppState>, id: String, key: String) -> Result<String, String> {
    with_vault(&state.vault, |v| v.reveal(&id, &key))
}

#[tauri::command]
fn copy_field(app: AppHandle, state: State<'_, AppState>, id: String, key: String, seconds: u64) -> Result<(), String> {
    let value = with_vault(&state.vault, |v| v.reveal(&id, &key))?;
    if value.is_empty() {
        return Err("EMPTY".into());
    }
    clipboard::copy_with_auto_clear(&app, value, seconds)
}

#[tauri::command]
fn copy_text(app: AppHandle, text: String, seconds: u64) -> Result<(), String> {
    clipboard::copy_with_auto_clear(&app, text, seconds)
}

#[tauri::command]
async fn save_entry(state: State<'_, AppState>, entry: EntryInput) -> Result<String, String> {
    let shared = state.vault.clone();
    blocking(move || with_vault(&shared, |v| v.save_entry(&entry))).await
}

#[tauri::command]
async fn delete_entry(state: State<'_, AppState>, id: String) -> Result<(), String> {
    let shared = state.vault.clone();
    blocking(move || with_vault(&shared, |v| v.delete(&id))).await
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let builder = tauri::Builder::default()
        .manage(AppState::default())
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_dialog::init());

    #[cfg(mobile)]
    let builder = builder.plugin(tauri_plugin_clipboard_manager::init());

    builder
        .invoke_handler(tauri::generate_handler![
            default_vault_path,
            vault_exists,
            create_vault,
            open_vault,
            lock_vault,
            list_entries,
            get_entry,
            reveal_field,
            copy_field,
            copy_text,
            save_entry,
            delete_entry,
        ])
        .run(tauri::generate_context!())
        .expect("KeyDone başlatılamadı");
}
