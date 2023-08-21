namespace Uploader.StorageProvider; 

// Declares the provider can refresh CDN
public interface ICdnRefresh { 
   Task RefreshObjectKeys(IEnumerable<string> objectKeys);
   Task RefreshRoot();
}