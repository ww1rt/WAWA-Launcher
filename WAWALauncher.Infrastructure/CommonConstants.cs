namespace WAWALauncher.Infrastructure
{
    public static class CommonConstants
    {
        public const string AssemblyVersionsUrl = "https://github.com/ww1rt/LAU/releases/download/latest/Assembly.json";

        public static string AssemblyVersionsFilePath { get =>  Path.Combine(GetAppDataPath(), "assembly.json"); } 

        private static string GetAppDataPath()
        {
            string baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(baseFolder, "WAWALauncher");
            Directory.CreateDirectory(appFolder);

            return appFolder;
        }
    }
}
