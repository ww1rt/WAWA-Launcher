using System;
using System.Collections.Generic;
using System.Text;

namespace WAWALauncher.Services.Models
{
    public class AssemblyModel
    {
        /// <summary>
        /// Gets or sets the name of assembly
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the core version 
        /// </summary>
        public string CoreVersion { get; set; }

        /// <summary>
        /// Gets or sets the core URL
        /// </summary>
        public string CoreURL { get; set; }

        /// <summary>
        /// Gets or sets the Modpack version 
        /// </summary>
        public string ModpackVersion { get; set; }

        /// <summary>
        /// Gets or sets the Modpack URL
        /// </summary>
        public string ModpackURL { get; set; }

        /// <summary>
        /// Gets or sets the Resources version 
        /// </summary>
        public string ResourcesVersion { get; set; }

        /// <summary>
        /// Gets or sets the Resources URL
        /// </summary>
        public string ResourcesURL { get; set; }

        /// <summary>
        /// Gets or sets the base game version
        /// </summary>
        public string ProfileId { get; set; }
    }

    /*
     
        {
            "Name": null,
            "CoreVersion": null,
            "CoreURL": null,
            "ModpackVersion": null,
            "ModpackURL": null,
            "ResourcesVersion": null,
            "ResourcesURL": null,
            "ProfileId": null
        }

     */
}
