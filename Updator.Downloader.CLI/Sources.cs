using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Updator.Downloader
{
    public class Source
    {
        public bool enable { get; set; }
        public string distributionUrl { get; set; }
    }

    public class Sources
    {
        // sources.json file version
        public int version { get; set; }
        // Update this sources.json file
        public string sourcesUrl { get; set; } 
        // Custom downloader update url, default is github
        public string customDownloaderUrl { get; set; } 

        public List<Source> sources { get; set; }
    }
}
