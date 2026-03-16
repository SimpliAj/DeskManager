using System.IO;

namespace DeskManager.Services;

public class FolderWatcherService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounceTimer;

    public event Action? FolderChanged;

    public void Watch(string folderPath)
    {
        _watcher?.Dispose();
        _debounceTimer?.Dispose();

        if (!Directory.Exists(folderPath))
            return;

        _debounceTimer = new System.Threading.Timer(_ => FolderChanged?.Invoke(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

        _watcher = new FileSystemWatcher(folderPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            EnableRaisingEvents = true
        };

        _watcher.Created += (_, _) => _debounceTimer?.Change(100, System.Threading.Timeout.Infinite);
        _watcher.Deleted += (_, _) => _debounceTimer?.Change(100, System.Threading.Timeout.Infinite);
        _watcher.Renamed += (_, _) => _debounceTimer?.Change(100, System.Threading.Timeout.Infinite);
    }

    public void Stop()
    {
        if (_watcher != null)
            _watcher.EnableRaisingEvents = false;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
    }
}
