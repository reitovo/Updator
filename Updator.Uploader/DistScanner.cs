using Microsoft.Extensions.Logging;

namespace Updator.Uploader;

public class DistItem {
   public FileInfo fileInfo;
   public string storageObjectKey;
}

// Scan distribution files
public class DistScanner {
   private DirectoryInfo _rootInfo;
   private List<DistItem> _items = new();
   private List<string> _ignored;
   private ILogger _logger;

   public DistScanner(string root, IEnumerable<string> ignored, ILogger logger) {
      _rootInfo = new DirectoryInfo(root);
      _ignored = new List<string>(ignored);
      _logger = logger;
   }

   public string RootFullName => _rootInfo.FullName;
   public DistItem[] Items => _items.ToArray();

   private void ScanDirectory(DirectoryInfo dir) {
      _logger.LogTrace($"Scanning directory {dir.FullName}");

      foreach (var fileInfo in dir.GetFiles()) {
         var objectKey = fileInfo.FullName.Replace("\\", "/").Replace($"{_rootInfo.FullName.Replace("\\", "/")}/", "");

         if (_ignored.Contains(objectKey)) {
            _logger.LogTrace($"Ignored file object key {objectKey}, path {fileInfo.FullName}");
            continue;
         }

         _items.Add(new DistItem() {
            fileInfo = fileInfo,
            storageObjectKey = objectKey
         });
         _logger.LogTrace($"Scanned file object key {objectKey}, path {fileInfo.FullName}");
      }

      foreach (var directoryInfo in dir.GetDirectories()) {
         var objectKey =
            $"{directoryInfo.FullName.Replace("\\", "/").Replace($"{_rootInfo.FullName.Replace("\\", "/")}/", "")}/";

         if (_ignored.Contains(objectKey)) {
            _logger.LogTrace($"Ignored directory object key {objectKey}, path {directoryInfo.FullName}");
            continue;
         }

         ScanDirectory(directoryInfo);
      }
   }

   public void Scan() {
      _logger.LogTrace($"Start scanning");

      _items.Clear();
      ScanDirectory(_rootInfo);

      _logger.LogTrace($"Scan complete, found {_items.Count} objects");
   }
}