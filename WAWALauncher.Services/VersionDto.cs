using System;
using System.Collections.Generic;
using System.Text;

namespace WAWALauncher.Services
{
    public class Assembly
    {
        public string Name { get; set; }
        public string CoreVersion {  get; set; }
        public string ModsVersion { get; set; }
        public string ResourcesVersion { get; set; }
        public string ProfileId { get; set; }
    }
}
