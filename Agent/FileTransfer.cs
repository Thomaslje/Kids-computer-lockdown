using KioskShared;

namespace KioskAgent;

/// <summary>Receives a file from the admin app in pieces. Pieces carry their offset, so a repeated piece is harmless.</summary>
static class FileTransfer
{
    public const string ShareFolder = @"C:\Share";

    static string Dir(string id) => Path.Combine(Paths.DataDir, "Uploads", Path.GetFileName(id));
    static UploadStart Meta(string id)
    {
        var f = Path.Combine(Dir(id), "meta.json");
        if (!File.Exists(f)) throw new InvalidOperationException("No upload in progress");
        return Json.From<UploadStart>(File.ReadAllText(f))!;
    }
    static string FilePath(UploadStart m) => Path.Combine(Dir(m.Id), m.FileName);

    public static void Start(UploadStart s)
    {
        var name = Path.GetFileName(s.FileName);
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Bad file name");
        var dir = Dir(s.Id);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "meta.json"), Json.To(s with { FileName = name }));
        using (File.Create(Path.Combine(dir, name))) { }
    }

    public static void Chunk(UploadChunk c)
    {
        var m = Meta(c.Id);
        var bytes = Convert.FromBase64String(c.B64);
        using var fs = new FileStream(FilePath(m), FileMode.Open, FileAccess.Write, FileShare.None);
        fs.Seek(c.Offset, SeekOrigin.Begin);
        fs.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Checks the file is complete and puts it where it belongs. Returns the final path.</summary>
    public static string Finish(string id)
    {
        var m = Meta(id);
        var path = FilePath(m);
        if (new FileInfo(path).Length != m.TotalBytes) throw new InvalidDataException("The upload is incomplete");
        if (m.Dest != "share") return path;

        EnsureShareFolder();
        var target = Path.Combine(ShareFolder, m.FileName);
        File.Move(path, target, true);
        Directory.Delete(Dir(id), true);
        return target;
    }

    /// <summary>The path of an uploaded file (for an install job).</summary>
    public static string PathOf(string id) => FilePath(Meta(id));
    public static void Discard(string id) { try { Directory.Delete(Dir(id), true); } catch { } }

    /// <summary>Creates C:\Share and lets everyone read and write in it.</summary>
    public static void EnsureShareFolder()
    {
        Directory.CreateDirectory(ShareFolder);
        Proc.Run("icacls.exe", false, ShareFolder, "/grant", "*S-1-1-0:(OI)(CI)M");
    }

    public static void CleanupAll()
    {
        try { var d = Path.Combine(Paths.DataDir, "Uploads"); if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
    }
}
