//! Panoya kopyalama ve belirli bir süre sonra panoyu otomatik temizleme.
//! Windows'ta kopyalanan bilgi pano geçmişine ve buluta gönderilmez.

use std::{
    sync::atomic::{AtomicU64, Ordering},
    thread,
    time::Duration,
};

use tauri::{AppHandle, Emitter, Runtime};
use zeroize::Zeroizing;

static GENERATION: AtomicU64 = AtomicU64::new(0);

pub fn copy_with_auto_clear<R: Runtime>(app: &AppHandle<R>, text: String, seconds: u64) -> Result<(), String> {
    let text = Zeroizing::new(text);
    write(app, &text).map_err(|e| format!("Panoya kopyalanamadı: {e}"))?;

    let generation = GENERATION.fetch_add(1, Ordering::SeqCst) + 1;
    let app = app.clone();
    let seconds = seconds.clamp(5, 600);
    thread::spawn(move || {
        thread::sleep(Duration::from_secs(seconds));
        // Bu sürede yeni bir şey kopyalandıysa onun zamanlayıcısı ilgilenir.
        if GENERATION.load(Ordering::SeqCst) != generation {
            return;
        }
        // Kullanıcı başka bir şey kopyaladıysa ona dokunma.
        let current = read(&app).map(Zeroizing::new);
        if current.as_deref().map(String::as_str) == Some(text.as_str()) && clear(&app).is_ok() {
            let _ = app.emit("clipboard-cleared", ());
        }
    });
    Ok(())
}

#[cfg(desktop)]
fn write<R: Runtime>(_app: &AppHandle<R>, text: &str) -> Result<(), String> {
    let mut clipboard = arboard::Clipboard::new().map_err(|e| e.to_string())?;
    #[cfg(windows)]
    {
        use arboard::SetExtWindows;
        clipboard
            .set()
            .exclude_from_history()
            .exclude_from_cloud()
            .exclude_from_monitoring()
            .text(text)
            .map_err(|e| e.to_string())
    }
    #[cfg(not(windows))]
    {
        clipboard.set_text(text).map_err(|e| e.to_string())
    }
}

#[cfg(desktop)]
fn read<R: Runtime>(_app: &AppHandle<R>) -> Option<String> {
    arboard::Clipboard::new().ok()?.get_text().ok()
}

#[cfg(desktop)]
fn clear<R: Runtime>(_app: &AppHandle<R>) -> Result<(), String> {
    arboard::Clipboard::new().and_then(|mut c| c.clear()).map_err(|e| e.to_string())
}

#[cfg(mobile)]
fn write<R: Runtime>(app: &AppHandle<R>, text: &str) -> Result<(), String> {
    use tauri_plugin_clipboard_manager::ClipboardExt;
    app.clipboard().write_text(text.to_string()).map_err(|e| e.to_string())
}

#[cfg(mobile)]
fn read<R: Runtime>(app: &AppHandle<R>) -> Option<String> {
    use tauri_plugin_clipboard_manager::ClipboardExt;
    app.clipboard().read_text().ok()
}

#[cfg(mobile)]
fn clear<R: Runtime>(app: &AppHandle<R>) -> Result<(), String> {
    use tauri_plugin_clipboard_manager::ClipboardExt;
    app.clipboard().clear().map_err(|e| e.to_string())
}
