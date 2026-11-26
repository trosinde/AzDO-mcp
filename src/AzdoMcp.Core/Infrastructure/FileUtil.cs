using System.Text;

namespace AzdoMcp.Core.Infrastructure;

public static class FileUtil
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes to a temp file next to the target and moves it over the target atomically.</summary>
    public static void WriteAllTextAtomic(string path, string content)
    {
        WriteAllBytesAtomic(path, Utf8NoBom.GetBytes(content));
    }

    public static void WriteAllBytesAtomic(string path, byte[] content)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(content);
                fs.Flush(flushToDisk: true);
            }

            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }
        }
    }

    /// <summary>Runs <paramref name="action"/> while holding a named, cross-process mutex.</summary>
    public static T WithMutex<T>(string name, Func<T> action)
    {
        using var mutex = new Mutex(initiallyOwned: false, name);
        var owned = false;
        try
        {
            try
            {
                owned = mutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }

            if (!owned)
            {
                throw new TimeoutException($"Sperre {name} nicht erhalten.");
            }

            return action();
        }
        finally
        {
            if (owned)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    public static void WithMutex(string name, Action action) => WithMutex(name, () =>
    {
        action();
        return 0;
    });
}
