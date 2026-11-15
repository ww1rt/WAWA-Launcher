using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WAWALauncher.Services
{
    /// <summary>
    /// Запускает уже установленную сборку NeoForge под Windows без скачивания файлов.
    /// </summary>
    public sealed class LauncherService : ILauncherService
    {
        /// <summary>Временная папка сборки по умолчанию до подключения настроек и менеджера ресурсов.</summary>
        private const string DefaultInstancePath = @"D:\LIR\chill-wawa";

        /// <summary>Идентификатор установленного профиля, используемый при пустом параметре.</summary>
        private const string DefaultProfileId = "1.21.1";

        /// <summary>Имя игрока для локального запуска, если пользователь не передал ник.</summary>
        private const string DefaultNickname = "Player";

        /// <summary>Максимальный размер кучи Java по умолчанию, в гигабайтах.</summary>
        private const int DefaultMemoryGb = 6;

        /// <summary>Начальный размер кучи Java, в гигабайтах.</summary>
        private const int InitialMemoryGb = 1;

        /// <summary>Минимально разрешённый пользователю лимит памяти, в гигабайтах.</summary>
        private const int MinimumMemoryGb = 2;

        /// <summary>Максимально разрешённый пользователю лимит памяти, в гигабайтах.</summary>
        private const int MaximumMemoryGb = 64;

        /// <summary>Разделитель полей существующего строкового формата параметров запуска.</summary>
        private const char ParameterSeparator = '|';

        /// <summary>Позиции полей в формате instancePath|profileId|ramGb|mainClass|nickname.</summary>
        private enum LaunchParameterIndex { InstancePath, ProfileId, MemoryGb, MainClass, Nickname }

        /// <summary>Допустимые символы идентификатора профиля; точки «.» и «..» проверяются отдельно.</summary>
        private const string ProfileIdPattern = @"^[a-zA-Z0-9_.-]+$";

        /// <summary>Ник Minecraft: от 1 до 16 латинских букв, цифр или знаков подчёркивания.</summary>
        private const string NicknamePattern = @"^[a-zA-Z0-9_]{1,16}$";

        /// <summary>Точка входа поддерживаемого полного профиля NeoForge.</summary>
        private const string ExpectedMainClass = "cpw.mods.bootstraplauncher.BootstrapLauncher";

        /// <summary>Каталог профилей относительно папки сборки.</summary>
        private const string VersionsDirectoryName = "versions";

        /// <summary>Каталог библиотек относительно папки сборки.</summary>
        private const string LibrariesDirectoryName = "libraries";

        /// <summary>Каталог распакованных нативных библиотек относительно папки профиля.</summary>
        private const string NativesDirectoryName = "natives";

        /// <summary>Каталог игровых ресурсов относительно папки сборки.</summary>
        private const string AssetsDirectoryName = "assets";

        /// <summary>Каталог индексов внутри каталога игровых ресурсов.</summary>
        private const string AssetIndexesDirectoryName = "indexes";

        /// <summary>Каталог логов относительно папки приложения.</summary>
        private const string LogsDirectoryName = "logs";

        /// <summary>Расширение JSON-файлов профиля и индекса ресурсов.</summary>
        private const string JsonFileExtension = ".json";

        /// <summary>Расширение архивов Java в Maven-координатах библиотек.</summary>
        private const string JavaArchiveExtension = ".jar";

        /// <summary>Префикс файлов, относящихся к одному запуску игры.</summary>
        private const string LaunchFilePrefix = "minecraft-";

        /// <summary>Формат локального времени в имени файла запуска, с точностью до миллисекунд.</summary>
        private const string LaunchTimestampFormat = "yyyyMMdd-HHmmss-fff";

        /// <summary>Расширение файла вывода процесса игры.</summary>
        private const string LogFileExtension = ".log";

        /// <summary>Расширение временного файла аргументов, который Java читает через @argfile.</summary>
        private const string ArgumentsFileExtension = ".args";

        /// <summary>Имя лаунчера для подстановки в аргументы профиля.</summary>
        private const string LauncherName = "WAWALauncher";

        /// <summary>Версия лаунчера для подстановки в аргументы профиля.</summary>
        private const string LauncherVersion = "1.0";

        /// <summary>Заглушка access token при локальном запуске без авторизации Microsoft.</summary>
        private const string OfflineAccessToken = "0";

        /// <summary>Заглушка Xbox User ID при локальном запуске.</summary>
        private const string OfflineXuid = "0";

        /// <summary>Тип пользователя при локальном запуске без авторизации.</summary>
        private const string OfflineUserType = "offline";

        /// <summary>Пустой JSON-объект свойств локального пользователя.</summary>
        private const string OfflineUserProperties = "{}";

        /// <summary>Тип версии, передаваемый текущей установленной сборке.</summary>
        private const string DefaultVersionType = "release";

        /// <summary>Префикс имени для совместимого с Minecraft вычисления offline UUID.</summary>
        private const string OfflineUuidPrefix = "OfflinePlayer:";

        /// <summary>Имя поддерживаемой ОС в правилах JSON-профиля.</summary>
        private const string SupportedOsName = "windows";

        /// <summary>Основное обозначение поддерживаемой архитектуры Java в правилах профиля.</summary>
        private const string SupportedArchitecture = "amd64";

        /// <summary>Альтернативное обозначение той же архитектуры x64.</summary>
        private const string SupportedArchitectureAlias = "x86_64";

        /// <summary>Основная версия Java, необходимая текущей сборке.</summary>
        private const int RequiredJavaVersion = 21;

        /// <summary>Требуемая разрядность Java для текущей сборки.</summary>
        private const int RequiredJavaBitness = 64;

        /// <summary>
        /// Сохранённое окно обнаружения раннего завершения процесса.
        /// Восемь секунд — текущая настройка, а не гарантия загрузки главного меню.
        /// </summary>
        private static readonly TimeSpan EarlyExitObservationPeriod = TimeSpan.FromSeconds(8);

        /// <summary>Предельное время ответа Java на запрос версии и свойств.</summary>
        private static readonly TimeSpan JavaProbeTimeout = TimeSpan.FromSeconds(15);

        /// <summary>Поддерживаемые пути встроенной Java относительно папки сборки, в порядке поиска.</summary>
        private static readonly string[] JavaRelativePaths =
        {
            Path.Combine("runtime", "jre", "java-runtime-delta", "windows-x64", "java-runtime-delta", "bin", "java.exe"),
            Path.Combine("runtime", "jre", "x64", "bin", "java.exe"),
            Path.Combine("runtime", "jre", "bin", "java.exe")
        };

        private static readonly SemaphoreSlim LaunchLock = new(1, 1);
        private static Process? _game;
        private static Task? _logTask;

        /// <summary>Подготавливает сборку, запускает Java и проверяет раннее завершение процесса.</summary>
        /// <param name="gameParams">
        /// Необязательные поля: instancePath|profileId|ramGb|mainClass|nickname.
        /// Пустые поля используют значения по умолчанию; mainClass берётся из JSON профиля.
        /// </param>
        /// <remarks>Завершение метода не подтверждает загрузку главного меню игры.</remarks>
        public async Task LaunchGame(string gameParams)
        {
            await LaunchLock.WaitAsync();
            try
            {
                await ResetPreviousLaunchAsync();
                ValidatePlatform();

                LaunchParameters parameters = ParseParameters(gameParams);
                ValidateParameters(parameters);
                InstallationPaths paths = CreateInstallationPaths(parameters);
                JsonElement profile = await LoadProfileAsync(paths.ProfileJson);
                string mainClass = ValidateProfile(profile, parameters.MainClass);
                string java = await FindJavaAsync(parameters.InstancePath);
                string assetIndex = ValidateInstallation(profile, paths);

                string classpath = BuildClasspath(profile, paths.Libraries);
                Dictionary<string, string> variables = CreateVariables(parameters, paths, assetIndex, classpath);
                List<string> arguments = BuildArguments(profile, mainClass, parameters.MemoryGb, variables);
                var (logPath, argsPath) = await WriteLaunchFilesAsync(java, parameters, arguments);

                _game = StartGame(java, parameters.InstancePath, argsPath);
                _logTask = CaptureOutput(_game, logPath, argsPath);
                await CheckEarlyExitAsync(_game, _logTask, logPath);
            }
            finally
            {
                LaunchLock.Release();
            }
        }

        /// <summary>Разобранные параметры запуска; проверяются перед обращением к файлам сборки.</summary>
        private sealed record LaunchParameters(
            string InstancePath, string ProfileId, int MemoryGb, string MainClass, string Nickname);

        /// <summary>Пути к файлам и каталогам одной выбранной установки.</summary>
        private sealed record InstallationPaths(string ProfileJson, string Libraries, string Natives, string Assets);

        private static async Task ResetPreviousLaunchAsync()
        {
            if (_game is { HasExited: false })
                throw new InvalidOperationException("Minecraft уже запущен этим лаунчером.");
            if (_logTask != null) await _logTask;
            _game?.Dispose();
            _game = null;
            _logTask = null;
        }

        private static void ValidatePlatform()
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Эта сборка настроена для Windows x64.");
        }

        /// <summary>Разбирает строковый формат, сохраняя значения по умолчанию для пустых полей.</summary>
        private static LaunchParameters ParseParameters(string? gameParams)
        {
            string[] parts = (gameParams ?? string.Empty).Split(ParameterSeparator);
            return new LaunchParameters(
                Path.GetFullPath(GetParameter(parts, LaunchParameterIndex.InstancePath, DefaultInstancePath)),
                GetParameter(parts, LaunchParameterIndex.ProfileId, DefaultProfileId),
                ParseMemory(GetParameter(parts, LaunchParameterIndex.MemoryGb, DefaultMemoryGb.ToString())),
                GetParameter(parts, LaunchParameterIndex.MainClass, string.Empty),
                GetParameter(parts, LaunchParameterIndex.Nickname, DefaultNickname));
        }

        private static string GetParameter(string[] parts, LaunchParameterIndex parameter, string fallback)
        {
            int index = (int)parameter;
            return index < parts.Length && !string.IsNullOrWhiteSpace(parts[index])
                ? parts[index].Trim() : fallback;
        }

        private static int ParseMemory(string value)
        {
            if (!int.TryParse(value, out int memoryGb))
                throw new ArgumentException($"Объём памяти должен быть от {MinimumMemoryGb} до {MaximumMemoryGb} ГБ.");
            return memoryGb;
        }

        private static void ValidateParameters(LaunchParameters parameters)
        {
            if (!Regex.IsMatch(parameters.ProfileId, ProfileIdPattern) || parameters.ProfileId is "." or "..")
                throw new ArgumentException("Некорректный ProfileId.");
            if (!Regex.IsMatch(parameters.Nickname, NicknamePattern))
                throw new ArgumentException("Ник: от 1 до 16 латинских букв, цифр или знаков _.");
            if (parameters.MemoryGb < MinimumMemoryGb || parameters.MemoryGb > MaximumMemoryGb)
                throw new ArgumentException($"Объём памяти должен быть от {MinimumMemoryGb} до {MaximumMemoryGb} ГБ.");
        }

        private static InstallationPaths CreateInstallationPaths(LaunchParameters parameters)
        {
            string versionFolder = Path.Combine(parameters.InstancePath, VersionsDirectoryName, parameters.ProfileId);
            return new InstallationPaths(
                Path.Combine(versionFolder, parameters.ProfileId + JsonFileExtension),
                Path.Combine(parameters.InstancePath, LibrariesDirectoryName),
                Path.Combine(versionFolder, NativesDirectoryName),
                Path.Combine(parameters.InstancePath, AssetsDirectoryName));
        }

        private static async Task<JsonElement> LoadProfileAsync(string jsonPath)
        {
            RequireFile(jsonPath);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
            // Копия остаётся доступной после освобождения JsonDocument.
            return json.RootElement.Clone();
        }

        private static string ValidateProfile(JsonElement profile, string requestedMainClass)
        {
            if (profile.TryGetProperty("inheritsFrom", out _))
                throw new InvalidDataException("Нужен полный JSON установленной сборки без inheritsFrom.");
            string mainClass = profile.GetProperty("mainClass").GetString()
                ?? throw new InvalidDataException("Отсутствует mainClass.");
            if (mainClass != ExpectedMainClass)
                throw new InvalidDataException("Выбранный профиль не является ожидаемым профилем NeoForge.");
            if (!string.IsNullOrEmpty(requestedMainClass) && requestedMainClass != mainClass)
                throw new ArgumentException("mainClass должен совпадать с JSON версии.");
            return mainClass;
        }

        private static string ValidateInstallation(JsonElement profile, InstallationPaths paths)
        {
            if (!Directory.Exists(paths.Natives))
                throw new DirectoryNotFoundException("Не найдены распакованные natives: " + paths.Natives);
            string assetIndex = profile.GetProperty("assetIndex").GetProperty("id").GetString()
                ?? throw new InvalidDataException("Отсутствует assetIndex.id.");
            RequireFile(Path.Combine(paths.Assets, AssetIndexesDirectoryName, assetIndex + JsonFileExtension));
            return assetIndex;
        }

        private static string BuildClasspath(JsonElement profile, string librariesDirectory)
        {
            var classpath = new List<string>();
            foreach (JsonElement library in profile.GetProperty("libraries").EnumerateArray())
            {
                if (!Allowed(library)) continue;
                string path = LibraryPath(librariesDirectory, library);
                RequireFile(path);
                // NeoForge сам загружает игровые client/universal/extra JAR.
                // Их нельзя добавлять в bootstrap classpath вместе с ванильным JAR.
                if (library.TryGetProperty("downloadOnly", out var downloadOnly) && downloadOnly.GetBoolean())
                    continue;
                classpath.Add(path);
            }
            return string.Join(Path.PathSeparator, classpath.Distinct());
        }

        private static Dictionary<string, string> CreateVariables(
            LaunchParameters parameters, InstallationPaths paths, string assetIndex, string classpath) => new()
        {
            ["classpath"] = classpath,
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["library_directory"] = paths.Libraries,
            ["natives_directory"] = paths.Natives,
            ["launcher_name"] = LauncherName,
            ["launcher_version"] = LauncherVersion,
            ["version_name"] = parameters.ProfileId,
            ["game_directory"] = parameters.InstancePath,
            ["assets_root"] = paths.Assets,
            ["assets_index_name"] = assetIndex,
            ["auth_player_name"] = parameters.Nickname,
            ["auth_uuid"] = OfflineUuid(parameters.Nickname),
            ["auth_access_token"] = OfflineAccessToken,
            ["clientid"] = Guid.Empty.ToString(),
            ["auth_xuid"] = OfflineXuid,
            ["user_type"] = OfflineUserType,
            ["user_properties"] = OfflineUserProperties,
            ["version_type"] = DefaultVersionType
        };

        /// <summary>Читает JVM- и игровые аргументы из JSON и подставляет параметры выбранной сборки.</summary>
        private static List<string> BuildArguments(
            JsonElement profile, string mainClass, int memoryGb, Dictionary<string, string> variables)
        {
            var arguments = new List<string> { $"-Xms{InitialMemoryGb}G", $"-Xmx{memoryGb}G" };
            JsonElement launchArguments = profile.GetProperty("arguments");
            AppendArguments(launchArguments.GetProperty("jvm"), arguments, variables);
            if (!arguments.Contains("-cp") && !arguments.Contains("-classpath"))
            {
                arguments.Add("-cp");
                arguments.Add(variables["classpath"]);
            }
            arguments.Add(mainClass);
            AppendArguments(launchArguments.GetProperty("game"), arguments, variables);
            return arguments;
        }

        /// <summary>Создаёт лог запуска и временный @argfile для обхода ограничения длины командной строки.</summary>
        private static async Task<(string LogPath, string ArgumentsPath)> WriteLaunchFilesAsync(
            string java, LaunchParameters parameters, IEnumerable<string> arguments)
        {
            string logDirectory = Path.Combine(AppContext.BaseDirectory, LogsDirectoryName);
            Directory.CreateDirectory(logDirectory);
            string fileName = LaunchFilePrefix + DateTime.Now.ToString(LaunchTimestampFormat);
            string logPath = Path.Combine(logDirectory, fileName + LogFileExtension);
            string argsPath = Path.Combine(logDirectory, fileName + ArgumentsFileExtension);
            await File.WriteAllLinesAsync(argsPath, arguments.Select(QuoteJavaArgument), new UTF8Encoding(false));
            await File.WriteAllTextAsync(logPath,
                $"Java: {java}{Environment.NewLine}Game directory: {parameters.InstancePath}{Environment.NewLine}" +
                $"Profile: {parameters.ProfileId}; RAM: {parameters.MemoryGb} GB; nickname: {parameters.Nickname}{Environment.NewLine}");
            return (logPath, argsPath);
        }

        private static ProcessStartInfo CreateJavaStartInfo(string java) => new(java)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        private static Process StartGame(string java, string folder, string argsPath)
        {
            ProcessStartInfo startInfo = CreateJavaStartInfo(java);
            startInfo.WorkingDirectory = folder;
            startInfo.ArgumentList.Add("@" + argsPath);
            try
            {
                return Process.Start(startInfo) ?? throw new InvalidOperationException("Java не запущена.");
            }
            catch
            {
                File.Delete(argsPath);
                throw;
            }
        }

        private static async Task CheckEarlyExitAsync(Process game, Task logTask, string logPath)
        {
            Task exit = game.WaitForExitAsync();
            if (await Task.WhenAny(exit, Task.Delay(EarlyExitObservationPeriod)) == exit)
            {
                await logTask;
                throw new InvalidOperationException($"Minecraft завершился (код {game.ExitCode}). Лог: {logPath}");
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
            // Maven-координаты имеют формат group:artifact:version[:classifier].
            string group = parts[0];
            string artifactName = parts[1];
            string version = parts[2];
            string classifierSuffix = parts.Length == 4 ? "-" + parts[3] : string.Empty;
            string filename = artifactName + "-" + version + classifierSuffix + JavaArchiveExtension;
            return Path.Combine(directory, group.Replace('.', Path.DirectorySeparatorChar), artifactName, version, filename);
        }

        private static bool Allowed(JsonElement element)
        {
            if (!element.TryGetProperty("rules", out var rules)) return true;
            bool allowed = false;
            foreach (JsonElement rule in rules.EnumerateArray())
            {
                if (!MatchesRule(rule)) continue;
                allowed = rule.GetProperty("action").GetString() == "allow";
            }
            return allowed;
        }

        private static bool MatchesRule(JsonElement rule)
        {
            if (rule.TryGetProperty("os", out var os))
            {
                if (os.TryGetProperty("name", out var name) && name.GetString() != SupportedOsName) return false;
                if (os.TryGetProperty("arch", out var arch) &&
                    arch.GetString() != SupportedArchitecture && arch.GetString() != SupportedArchitectureAlias) return false;
                if (os.TryGetProperty("version", out var version) &&
                    !Regex.IsMatch(Environment.OSVersion.Version.ToString(), version.GetString()!)) return false;
            }
            // Для обычного запуска demo/quickPlay/customResolution выключены.
            return !rule.TryGetProperty("features", out var features) ||
                !features.EnumerateObject().Any(feature => feature.Value.GetBoolean());
        }

        private static void AppendArguments(JsonElement array, List<string> target, Dictionary<string, string> variables)
        {
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && !Allowed(item)) continue;
                JsonElement value = item.ValueKind == JsonValueKind.String ? item : item.GetProperty("value");
                if (value.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement part in value.EnumerateArray())
                        target.Add(ExpandArgument(part.GetString()!, variables));
                }
                else target.Add(ExpandArgument(value.GetString()!, variables));
            }
        }

        /// <summary>Подставляет значения переменных вида ${name} из JSON-профиля.</summary>
        private static string ExpandArgument(string argument, Dictionary<string, string> variables) =>
            Regex.Replace(argument, @"\$\{(?<name>[^}]+)\}", match =>
                variables.TryGetValue(match.Groups["name"].Value, out string? value) ? value :
                throw new InvalidDataException("Неизвестный параметр JSON: " + match.Value));

        private static async Task<string> FindJavaAsync(string folder)
        {
            IEnumerable<string> candidates = JavaRelativePaths.Select(path => Path.Combine(folder, path));
            foreach (string candidate in candidates.Where(File.Exists))
            {
                if (await IsCompatibleJavaAsync(candidate)) return candidate;
            }
            throw new FileNotFoundException($"В runtime сборки не найдена рабочая Java {RequiredJavaVersion} x{RequiredJavaBitness}.");
        }

        private static async Task<bool> IsCompatibleJavaAsync(string java)
        {
            ProcessStartInfo info = CreateJavaStartInfo(java);
            info.ArgumentList.Add("-XshowSettings:properties");
            info.ArgumentList.Add("-version");
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Не удалось проверить Java.");
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(JavaProbeTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(true);
                await process.WaitForExitAsync();
                throw new TimeoutException("Java не ответила на проверку версии.");
            }
            string output = await stderr + await stdout;
            return process.ExitCode == 0 &&
                Regex.IsMatch(output, $@"java.specification.version\s*=\s*{RequiredJavaVersion}\s") &&
                Regex.IsMatch(output, $@"sun.arch.data.model\s*=\s*{RequiredJavaBitness}\s");
        }

        /// <summary>Вычисляет UUID v3 (MD5) с вариантом RFC 4122 по алгоритму offline-профилей Minecraft.</summary>
        private static string OfflineUuid(string nickname)
        {
            byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(OfflineUuidPrefix + nickname));
            // RFC 4122: старшие 4 бита байта 6 — версия 3, старшие 2 бита байта 8 — вариант 10.
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
            try
            {
                await Task.WhenAll(
                    PumpOutputAsync(game.StandardOutput, writer, gate, string.Empty),
                    PumpOutputAsync(game.StandardError, writer, gate, "[stderr] "));
                await game.WaitForExitAsync();
                await writer.WriteLineAsync("Exit code: " + game.ExitCode);
            }
            finally
            {
                File.Delete(argsPath);
            }
        }

        private static async Task PumpOutputAsync(StreamReader reader, StreamWriter writer, SemaphoreSlim gate, string prefix)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                await gate.WaitAsync();
                try { await writer.WriteLineAsync(prefix + line); }
                finally { gate.Release(); }
            }
        }
    }
}
