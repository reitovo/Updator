namespace Updator.Common.StorageProvider; 

// Declares the provider can refresh CDN
public interface ICdnRefresh { 
   Task CdnPrefetchObjectKeys(IEnumerable<string> objectKeys);
   Task CdnPurgePath();
}