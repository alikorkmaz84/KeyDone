using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Utility;

namespace KeyDone.Core;

public sealed record EntrySummary(string Id, string Kind, string Title, string UserName);

public sealed record EntryField(string Key, string Value, bool IsProtected);

/// <summary>Bir kaydın tüm alanları. Id boşsa yeni kayıttır.</summary>
public sealed record EntryData(string? Id, string Kind, IReadOnlyList<EntryField> Fields)
{
    public string Title => Fields.FirstOrDefault(f => f.Key == StandardFields.Title)?.Value ?? "";

    public string Value(string key) => Fields.FirstOrDefault(f => f.Key == key)?.Value ?? "";
}

/// <summary>Kullanıcıya olduğu gibi gösterilebilecek Türkçe hata.</summary>
public sealed class VaultException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>KeePass (KDBX 4) kasası.</summary>
public sealed class Vault : IDisposable
{
    /// <summary>Kayıt türünün saklandığı özel veri anahtarı. Diğer KeePass uygulamaları bunu görmezden gelir.</summary>
    public const string KindKey = "KeyDone.Kind";

    readonly PwDatabase _db;

    public string FilePath { get; }

    Vault(PwDatabase db, string path)
    {
        _db = db;
        FilePath = path;
    }

    /// <summary>Yeni kasalar için Argon2id, 64 MiB bellek. Masaüstünde yaklaşık yarım saniye sürer.</summary>
    public static KdfParameters DefaultKdf() => Argon2id(iterations: 10, memoryBytes: 64UL * 1024 * 1024, parallelism: 2);

    public static KdfParameters Argon2id(ulong iterations, ulong memoryBytes, uint parallelism)
    {
        var parameters = new Argon2Kdf(Argon2Type.ID).GetDefaultParameters();
        parameters.SetUInt64(Argon2Kdf.ParamIterations, iterations);
        parameters.SetUInt64(Argon2Kdf.ParamMemory, memoryBytes);
        parameters.SetUInt32(Argon2Kdf.ParamParallelism, parallelism);
        return parameters;
    }

    static CompositeKey KeyFrom(string password)
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(password));
        return key;
    }

    public static Vault Create(string path, string password, KdfParameters? kdf = null)
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(path), KeyFrom(password));
        db.KdfParameters = kdf ?? DefaultKdf();
        db.Name = "KeyDone";
        db.RootGroup.Name = "KeyDone";
        db.UseFileTransactions = true;
        var vault = new Vault(db, path);
        vault.Save();
        return vault;
    }

    public static Vault Open(string path, string password)
    {
        if (!File.Exists(path))
            throw new VaultException("Kasa dosyası bulunamadı.");
        var db = new PwDatabase();
        try
        {
            db.Open(IOConnectionInfo.FromPath(path), KeyFrom(password), null);
        }
        catch (InvalidCompositeKeyException)
        {
            throw new VaultException("Ana şifre yanlış.");
        }
        catch (Exception ex)
        {
            throw new VaultException("Kasa açılamadı. Dosya bir KeePass kasası olmayabilir ya da bozulmuş olabilir.", ex);
        }
        // Önce geçici dosyaya yazılır, sonra asıl dosyanın yerine konur; yazma yarıda kalırsa kasa bozulmaz.
        db.UseFileTransactions = true;
        return new Vault(db, path);
    }

    public void Save()
    {
        try
        {
            _db.Save(null);
        }
        catch (Exception ex)
        {
            throw new VaultException($"Kasa kaydedilemedi: {ex.Message}", ex);
        }
    }

    static string IdOf(PwEntry entry) => entry.Uuid.ToHexString();

    static string KindOf(PwEntry entry) => entry.CustomData.Get(KindKey) ?? "";

    PwEntry Find(string id)
    {
        PwEntry? entry = null;
        try
        {
            entry = _db.RootGroup.FindEntry(new PwUuid(MemUtil.HexStringToByteArray(id)), true);
        }
        catch (Exception)
        {
            // geçersiz kimlik
        }
        return entry ?? throw new VaultException("Kayıt bulunamadı.");
    }

    bool InRecycleBin(PwEntry entry)
    {
        if (_db.RecycleBinUuid.Equals(PwUuid.Zero)) return false;
        for (var group = entry.ParentGroup; group != null; group = group.ParentGroup)
            if (group.Uuid.Equals(_db.RecycleBinUuid)) return true;
        return false;
    }

    public IReadOnlyList<EntrySummary> List() =>
        _db.RootGroup.GetEntries(true)
            .Where(e => !InRecycleBin(e))
            .Select(e => new EntrySummary(
                IdOf(e),
                KindOf(e),
                e.Strings.ReadSafe(PwDefs.TitleField),
                e.Strings.ReadSafe(PwDefs.UserNameField)))
            .ToList();

    public EntryData Get(string id)
    {
        var entry = Find(id);
        var fields = entry.Strings
            .Select(kv => new EntryField(kv.Key, kv.Value.ReadString(), kv.Value.IsProtected))
            .ToList();
        return new EntryData(id, KindOf(entry), fields);
    }

    public string Read(string id, string key) => Find(id).Strings.ReadSafe(key);

    /// <summary>Kaydı ekler ya da günceller ve kasayı kaydeder. Kaydın kimliğini döndürür.</summary>
    public string SaveEntry(EntryData data)
    {
        if (string.IsNullOrWhiteSpace(data.Title))
            throw new VaultException("Kayıt adı boş bırakılamaz.");

        PwEntry entry;
        bool isNew = data.Id is null;
        if (isNew)
        {
            entry = new PwEntry(true, true);
            _db.RootGroup.AddEntry(entry, true);
        }
        else
        {
            entry = Find(data.Id!);
            // Eski hal, KeePass'teki gibi kaydın geçmişine eklenir.
            entry.CreateBackup(_db);
        }

        foreach (var field in data.Fields)
        {
            if (field.Value.Length == 0 && !StandardFields.IsStandard(field.Key))
            {
                entry.Strings.Remove(field.Key);
                continue;
            }
            bool isProtected = field.IsProtected || field.Key == StandardFields.Password;
            entry.Strings.Set(field.Key, new ProtectedString(isProtected, field.Value));
        }
        entry.CustomData.Set(KindKey, data.Kind);
        entry.Touch(true, false);
        entry.MaintainBackups(_db);

        try
        {
            Save();
        }
        catch when (isNew)
        {
            _db.RootGroup.Entries.Remove(entry);
            throw;
        }
        return IdOf(entry);
    }

    public void Delete(string id)
    {
        var entry = Find(id);
        var parent = entry.ParentGroup;
        var deleted = new PwDeletedObject(entry.Uuid, DateTime.UtcNow);
        parent.Entries.Remove(entry);
        _db.DeletedObjects.Add(deleted);
        try
        {
            Save();
        }
        catch
        {
            _db.DeletedObjects.Remove(deleted);
            parent.AddEntry(entry, true);
            throw;
        }
    }

    public void Dispose() => _db.Close();
}
