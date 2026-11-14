using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WAWALauncher.Services
{
    // Запуск уже установленной сборки NeoForge под Windows, без скачивания файлов.
    public sealed class LauncherService : ILauncherService
    {
        private static readonly SemaphoreSlim LaunchLock = new(1, 1);
        private static Process? _game;
        private static Task? _logTask;

        // Пустая строка использует эту сборку. Необязательный формат:
        // instancePath|profileId|ramGb|mainClass (пусто = из JSON)|nickname
        public async Task LaunchGame(string gameParams)
        {
            await LaunchLock.WaitAsync();
            try
            {
                if (_game is { HasExited: false })
                    throw new InvalidOperationException("Minecraft уже запущен этим лаунчером.");
                if (_logTask != null) await _logTask;
                _game?.Dispose();
                _game = null;

                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException("Эта сборка настроена для Windows x64.");

                string[] parts = (gameParams ?? "").Split('|');
                string Part(int index, string fallback) =>
                    index < parts.Length && !string.IsNullOrWhiteSpace(parts[index])
                        ? parts[index].Trim() : fallback;

                string folder = Path.GetFullPath(Part(0, @"D:\LIR\chill-wawa"));
                string profile = Part(1, "1.21.1");
                string nickname = Part(4, "Player");
                if (!Regex.IsMatch(profile, @"^[a-zA-Z0-9_.-]+$") || profile is "." or "..")
                    throw new ArgumentException("Некорректный ProfileId.");
                if (!Regex.IsMatch(nickname, @"^[a-zA-Z0-9_]{1,16}$"))
                    throw new ArgumentException("Ник: от 1 до 16 латинских букв, цифр или знаков _.");
                if (!int.TryParse(Part(2, "6"), out int ram) || ram < 2 || ram > 64)
                    throw new ArgumentException("Объём памяти должен быть от 2 до 64 ГБ.");

                string versionFolder = Path.Combine(folder, "versions", profile);
                string jsonPath = Path.Combine(versionFolder, profile + ".json");
                RequireFile(jsonPath);
                using var json = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
                JsonElement root = json.RootElement;
                if (root.TryGetProperty("inheritsFrom", out _))
                    throw new InvalidDataException("Нужен полный JSON установленной сборки, как в chill-wawa.");
                string mainClass = root.GetProperty("mainClass").GetString()
                    ?? throw new InvalidDataException("Отсутствует mainClass.");
                if (mainClass != "cpw.mods.bootstraplauncher.BootstrapLauncher")
                    throw new InvalidDataException("Выбранный профиль не является ожидаемым профилем NeoForge.");
                if (Part(3, mainClass) != mainClass)
                    throw new ArgumentException("mainClass должен совпадать с JSON версии.");

                string java = await FindJava21(folder);
                string libraries = Path.Combine(folder, "libraries");
                string natives = Path.Combine(versionFolder, "natives");
                if (!Directory.Exists(natives))
                    throw new DirectoryNotFoundException("Не найдены распакованные natives: " + natives);
                string assetIndex = root.GetProperty("assetIndex").GetProperty("id").GetString()
                    ?? throw new InvalidDataException("Отсутствует assetIndex.id.");
                RequireFile(Path.Combine(folder, "assets", "indexes", assetIndex + ".json"));

                var classpath = new List<string>();
                foreach (JsonElement library in root.GetProperty("libraries").EnumerateArray())
                {
                    if (!Allowed(library)) continue;
                    string path = LibraryPath(libraries, library);
                    RequireFile(path);
                    // NeoForge сам загружает игровые client/universal/extra JAR.
                    // Их нельзя добавлять в bootstrap classpath вместе с ванильным JAR.
                    if (library.TryGetProperty("downloadOnly", out var downloadOnly) && downloadOnly.GetBoolean())
                        continue;
                    classpath.Add(path);
                }

                var variables = new Dictionary<string, string>
                {
                    ["classpath"] = string.Join(Path.PathSeparator, classpath.Distinct()),
                    ["classpath_separator"] = Path.PathSeparator.ToString(),
                    ["library_directory"] = libraries,
                    ["natives_directory"] = natives,
                    ["launcher_name"] = "WAWALauncher",
                    ["launcher_version"] = "1.0",
                    ["version_name"] = profile,
                    ["game_directory"] = folder,
                    ["assets_root"] = Path.Combine(folder, "assets"),
                    ["assets_index_name"] = assetIndex,
                    ["auth_player_name"] = nickname,
                    ["auth_uuid"] = OfflineUuid(nickname),
                    ["auth_access_token"] = "0",
                    ["clientid"] = Guid.Empty.ToString(),
                    ["auth_xuid"] = "0",
                    ["user_type"] = "offline",
                    ["user_properties"] = "{}",
                    ["version_type"] = "release"
                };
                var arguments = new List<string> { "-Xms1G", $"-Xmx{ram}G" };
                JsonElement launchArguments = root.GetProperty("arguments");
                AppendArguments(launchArguments.GetProperty("jvm"), arguments, variables);
                if (!arguments.Contains("-cp") && !arguments.Contains("-classpath"))
                {
                    arguments.Add("-cp");
                    arguments.Add(variables["classpath"]);
                }
                arguments.Add(mainClass);
                AppendArguments(launchArguments.GetProperty("game"), arguments, variables);

                string logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(logDirectory);
                string logPath = Path.Combine(logDirectory, "minecraft-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".log");
                // @argfile обходит ограничение длины командной строки Windows.
                string argsPath = Path.ChangeExtension(logPath, ".args");
                await File.WriteAllLinesAsync(argsPath, arguments.Select(QuoteJavaArgument), new UTF8Encoding(false));
                await File.WriteAllTextAsync(logPath,
                    $"Java: {java}{Environment.NewLine}Game directory: {folder}{Environment.NewLine}" +
                    $"Profile: {profile}; RAM: {ram} GB; nickname: {nickname}{Environment.NewLine}");

                var startInfo = new ProcessStartInfo(java)
                {
                    WorkingDirectory = folder,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                startInfo.ArgumentList.Add("@" + argsPath);
                try
                {
                    _game = Process.Start(startInfo) ?? throw new InvalidOperationException("Java не запущена.");
                }
                catch
                {
                    File.Delete(argsPath);
                    throw;
                }
                _logTask = CaptureOutput(_game, logPath, argsPath);

                // Это проверка раннего сбоя, а не доказательство загрузки главного меню.
                Task exit = _game.WaitForExitAsync();
                if (await Task.WhenAny(exit, Task.Delay(TimeSpan.FromSeconds(8))) == exit)
                {
                    await _logTask;
                    throw new InvalidOperationException($"Minecraft завершился (код {_game.ExitCode}). Лог: {logPath}");
                }
            }
            finally
            {
                LaunchLock.Release();
            }
        }

        private static string LibraryPath(string directory, JsonElement library)
        {
            if (library.TryGetProperty("downloads", out var downloads) &&
                downloads.TryGetProperty("artifact", out var artifact) &&
                artifact.TryGetProperty("path", out var path))
                return Path.Combine(directory, path.GetString()!.Replace('/', Path.DirectorySeparatorChar));

            string coordinate = library.GetProperty("name").GetString()!;
            string[] parts = coordinate.Split(':');
            if (parts.Length is < 3 or > 4)
                throw new InvalidDataException("Неизвестный формат библиотеки: " + coordinate);
            string filename = parts[1] + "-" + parts[2] + (parts.Length == 4 ? "-" + parts[3] : "") + ".jar";
            return Path.Combine(directory, parts[0].Replace('.', Path.DirectorySeparatorChar), parts[1], parts[2], filename);
        }

        private static bool Allowed(JsonElement element)
        {
            if (!element.TryGetProperty("rules", out var rules)) return true;
            bool allowed = false;
            foreach (JsonElement rule in rules.EnumerateArray())
            {
                if (rule.TryGetProperty("os", out var os))
                {
                    if (os.TryGetProperty("name", out var name) && name.GetString() != "windows") continue;
                    if (os.TryGetProperty("arch", out var arch) && arch.GetString() != "amd64" && arch.GetString() != "x86_64") continue;
                    if (os.TryGetProperty("version", out var version) &&
                        !Regex.IsMatch(Environment.OSVersion.Version.ToString(), version.GetString()!)) continue;
                }
                // Для обычного запуска demo/quickPlay/customResolution выключены.
                if (rule.TryGetProperty("features", out var features) &&
                    features.EnumerateObject().Any(feature => feature.Value.GetBoolean())) continue;
                allowed = rule.GetProperty("action").GetString() == "allow";
            }
            return allowed;
        }

        private static void AppendArguments(JsonElement array, List<string> target, Dictionary<string, string> variables)
        {
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && !Allowed(item)) continue;
                JsonElement value = item.ValueKind == JsonValueKind.String ? item : item.GetProperty("value");
                if (value.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement part in value.EnumerateArray()) Add(part.GetString()!);
                }
                else Add(value.GetString()!);
            }

            void Add(string argument)
            {
                string expanded = Regex.Replace(argument, @"\$\{([^}]+)\}", match =>
                    variables.TryGetValue(match.Groups[1].Value, out string? value) ? value :
                    throw new InvalidDataException("Неизвестный параметр JSON: " + match.Value));
                target.Add(expanded);
            }
        }

        private static async Task<string> FindJava21(string folder)
        {
            string[] candidates =
            {
                Path.Combine(folder, "runtime", "jre", "java-runtime-delta", "windows-x64", "java-runtime-delta", "bin", "java.exe"),
                Path.Combine(folder, "runtime", "jre", "x64", "bin", "java.exe"),
                Path.Combine(folder, "runtime", "jre", "bin", "java.exe")
            };
            foreach (string candidate in candidates.Where(File.Exists))
            {
                var info = new ProcessStartInfo(candidate)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardError = true, RedirectStandardOutput = true
                };
                info.ArgumentList.Add("-XshowSettings:properties");
                info.ArgumentList.Add("-version");
                using var process = Process.Start(info) ?? throw new InvalidOperationException("Не удалось проверить Java.");
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync();
                    throw new TimeoutException("Java не ответила на проверку версии.");
                }
                string output = await stderr + await stdout;
                if (process.ExitCode == 0 && Regex.IsMatch(output, @"java.specification.version\s*=\s*21\s") &&
                    Regex.IsMatch(output, @"sun.arch.data.model\s*=\s*64\s")) return candidate;
            }
            throw new FileNotFoundException("В runtime сборки не найдена рабочая Java 21 x64.");
        }

        private static string OfflineUuid(string nickname)
        {
            byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + nickname));
            hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
            hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
            return Guid.ParseExact(Convert.ToHexString(hash), "N").ToString();
        }

        private static string QuoteJavaArgument(string argument) =>
            "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

        private static void RequireFile(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Отсутствует файл сборки: " + path, path);
        }

        private static async Task CaptureOutput(Process game, string path, string argsPath)
        {
            using var writer = new StreamWriter(path, append: true) { AutoFlush = true };
            using var gate = new SemaphoreSlim(1, 1);
            async Task Pump(StreamReader reader, string prefix)
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    await gate.WaitAsync();
                    try { await writer.WriteLineAsync(prefix + line); }
                    finally { gate.Release(); }
                }
            }
            try
            {
                await Task.WhenAll(Pump(game.StandardOutput, ""), Pump(game.StandardError, "[stderr] "));
                await game.WaitForExitAsync();
                await writer.WriteLineAsync("Exit code: " + game.ExitCode);
            }
            finally
            {
                File.Delete(argsPath);
            }
        }
    }
}
