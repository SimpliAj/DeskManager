using System;
using System.IO;

namespace DeskManager.Services;

/// Service to manage moving files to/from grid storage directory
public class FileStorageService
{
    private readonly string _storageDirectory;

    public FileStorageService()
    {
        // Create storage directory in AppData
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _storageDirectory = Path.Combine(appDataPath, "DeskManager", "Items");
        
        if (!Directory.Exists(_storageDirectory))
        {
            Directory.CreateDirectory(_storageDirectory);
            System.Diagnostics.Debug.WriteLine($"Created storage directory: {_storageDirectory}");
        }
    }

    /// Move a file to grid storage and return the new path
    /// Returns originalPath if move failed (indicating failure to caller)
    public string MoveToStorage(string originalPath)
    {
        try
        {
            if (!File.Exists(originalPath) && !Directory.Exists(originalPath))
            {
                System.Diagnostics.Debug.WriteLine($"❌ File doesn't exist: {originalPath}");
                return originalPath; // Signal failure
            }

            string fileName = Path.GetFileName(originalPath);
            string storagePath = Path.Combine(_storageDirectory, fileName);
            
            System.Diagnostics.Debug.WriteLine($"🔄 MoveToStorage attempting: {originalPath} → {storagePath}");

            // If file already exists in storage, create a unique name
            if (File.Exists(storagePath) || Directory.Exists(storagePath))
            {
                string baseName = Path.GetFileNameWithoutExtension(fileName);
                string extension = Path.GetExtension(fileName);
                int counter = 1;
                
                while (File.Exists(storagePath) || Directory.Exists(storagePath))
                {
                    string newFileName = $"{baseName}_{counter}{extension}";
                    storagePath = Path.Combine(_storageDirectory, newFileName);
                    counter++;
                }
                System.Diagnostics.Debug.WriteLine($"⚠️ Target exists, using unique name: {storagePath}");
            }

            // Move file/directory to storage
            if (File.Exists(originalPath))
            {
                // For files: simple move
                File.Move(originalPath, storagePath);
                System.Diagnostics.Debug.WriteLine($"✅ Moved FILE to storage: {originalPath} → {storagePath}");
                return storagePath;
            }
            else if (Directory.Exists(originalPath))
            {
                // For directories: try move, but with error handling
                try
                {
                    Directory.Move(originalPath, storagePath);
                    System.Diagnostics.Debug.WriteLine($"✅ Moved DIRECTORY to storage: {originalPath} → {storagePath}");
                    return storagePath;
                }
                catch (IOException ioEx)
                {
                    // Folder move blocked (OneDrive sync, locked files, permissions)
                    System.Diagnostics.Debug.WriteLine($"⚠️ DIRECTORY MOVE BLOCKED - {ioEx.Message}");
                    return originalPath; // Signal failure - item should NOT be added
                }
            }

            System.Diagnostics.Debug.WriteLine($"❌ MoveToStorage failed - file disappeared during process");
            return originalPath; // Signal failure
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Exception in MoveToStorage: {ex.GetType().Name} - {ex.Message}");
            return originalPath; // Signal failure - always return original if error
        }
    }

    /// Move a file from grid storage back to original location
    public bool MoveFromStorage(string storagePath, string originalPath)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"🔄 MoveFromStorage attempting: {storagePath} → {originalPath}");
            
            if (!File.Exists(storagePath) && !Directory.Exists(storagePath))
            {
                System.Diagnostics.Debug.WriteLine($"❌ File in storage doesn't exist: {storagePath}");
                return false;
            }

            // Ensure original directory exists
            string originalDir = Path.GetDirectoryName(originalPath);
            if (string.IsNullOrEmpty(originalDir))
            {
                System.Diagnostics.Debug.WriteLine($"❌ Could not get directory from path: {originalPath}");
                return false;
            }
            
            if (!Directory.Exists(originalDir))
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"📁 Original directory doesn't exist, creating: {originalDir}");
                    Directory.CreateDirectory(originalDir);
                    System.Diagnostics.Debug.WriteLine($"  → Directory created successfully");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"  ❌ Failed to create directory: {ex.Message}");
                    return false;
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"📁 Original directory exists: {originalDir}");
            }

            // Move back to original location
            if (File.Exists(storagePath))
            {
                // If file already exists at original location, DELETE it first and then move
                if (File.Exists(originalPath))
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ File already exists at original location, replacing: {originalPath}");
                    try
                    {
                        File.Delete(originalPath);
                        System.Diagnostics.Debug.WriteLine($"  → Deleted old file");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"  ❌ Failed to delete old file: {ex.Message}");
                        return false;
                    }
                }
                
                File.Move(storagePath, originalPath);
                System.Diagnostics.Debug.WriteLine($"✅ Moved FILE from storage: {storagePath} → {originalPath}");
                return true;
            }
            else if (Directory.Exists(storagePath))
            {
                // If directory already exists at original location, DELETE it first and then move
                if (Directory.Exists(originalPath))
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Directory already exists at original location, replacing: {originalPath}");
                    try
                    {
                        Directory.Delete(originalPath, recursive: true);
                        System.Diagnostics.Debug.WriteLine($"  → Deleted old directory");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"  ❌ Failed to delete old directory: {ex.Message}");
                        return false;
                    }
                }
                
                Directory.Move(storagePath, originalPath);
                System.Diagnostics.Debug.WriteLine($"✅ Moved DIRECTORY from storage: {storagePath} → {originalPath}");
                return true;
            }

            System.Diagnostics.Debug.WriteLine($"❌ MoveFromStorage failed - file disappeared during process");
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Exception in MoveFromStorage: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    /// Get the storage directory path
    public string StorageDirectory => _storageDirectory;
}
