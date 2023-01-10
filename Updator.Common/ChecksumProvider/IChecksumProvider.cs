namespace Updator.Common.ChecksumProvider; 

public interface IChecksumProvider { 
   Task<string> CalculateChecksum(Stream fileStream);
}