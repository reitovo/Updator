namespace Uploader.StorageProvider; 

public interface ICdnRefresh { 
   Task RefreshObjectKeys(IEnumerable<string> objectKeys);
}