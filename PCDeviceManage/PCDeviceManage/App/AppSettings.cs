using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCDeviceManage.App
{
    /// <summary>
    /// 用户配置：端口、监听地址、frp 开关保存在程序目录下的 settings.json，
    /// 访问令牌单独保存在程序目录下的 access_token，日志写在程序目录下的 Log\。
    /// 所有数据都和程序放在一起，程序目录需要有写权限。
    /// </summary>
    public sealed class AppSettings
    {
        public const int DefaultWebPort = 9527;
        public const string DefaultWebHost = "127.0.0.1";

        [JsonPropertyName("webHost")]
        public string WebHost { get; set; } = DefaultWebHost;

        [JsonPropertyName("webPort")]
        public int WebPort { get; set; } = DefaultWebPort;

        /// <summary>访问令牌，来自程序目录下的 access_token 文件，不写入 settings.json。</summary>
        [JsonIgnore]
        public string AccessToken { get; set; }

        /// <summary>旧版本写在 settings.json 里的令牌，只用于迁移，迁移后从文件中删除。</summary>
        [JsonPropertyName("accessToken")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string LegacyAccessToken { get; set; }

        /// <summary>frp\frpc.exe 和 frp\frpc.ini 都存在时是否启动 frp。</summary>
        [JsonPropertyName("frpEnabled")]
        public bool FrpEnabled { get; set; } = true;

        // 配置文件允许手工编辑：忽略大小写、允许注释和末尾逗号。
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true
        };

        /// <summary>配置文件：程序目录下的 settings.json。</summary>
        public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

        /// <summary>访问令牌文件：程序目录下的 access_token。</summary>
        public static string DefaultTokenPath => Path.Combine(AppContext.BaseDirectory, "access_token");

        /// <summary>日志目录：程序目录下的 Log（与 log4net.config 中的配置一致）。</summary>
        public static string LogDirectory => Path.Combine(AppContext.BaseDirectory, "Log");

        /// <summary>旧版本存放配置、令牌和日志的目录，迁移到程序目录后整个删除。</summary>
        public static string LegacyDataDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCManage");

        /// <summary>
        /// 读取配置和访问令牌。程序目录没有 settings.json 时，从 legacyDirectory 中的 settings.json 迁移。
        /// 令牌按顺序取自：tokenPath、settings.json 中旧的 accessToken、legacyDirectory 中的 access_token，
        /// 都没有则生成一个；不是从 tokenPath 读到的令牌会写入 tokenPath。
        /// 配置文件存在但无法解析时抛出异常，而不是悄悄生成新令牌覆盖用户配置。
        /// </summary>
        public static AppSettings LoadOrCreate(string path, string tokenPath, string legacyDirectory, out bool tokenCreated)
        {
            tokenCreated = false;
            bool dirty = false;
            AppSettings settings;
            string legacySettingsPath = legacyDirectory == null ? null : Path.Combine(legacyDirectory, "settings.json");
            string legacyTokenPath = legacyDirectory == null ? null : Path.Combine(legacyDirectory, "access_token");

            if (File.Exists(path))
            {
                settings = Read(path);
            }
            else if (legacySettingsPath != null && File.Exists(legacySettingsPath))
            {
                settings = Read(legacySettingsPath);
                dirty = true;
            }
            else
            {
                settings = new AppSettings();
                dirty = true;
            }

            if (settings.WebPort < 1 || settings.WebPort > 65535)
            {
                settings.WebPort = DefaultWebPort;
                dirty = true;
            }
            if (string.IsNullOrWhiteSpace(settings.WebHost))
            {
                settings.WebHost = DefaultWebHost;
                dirty = true;
            }
            settings.AccessToken = ReadToken(tokenPath);
            if (settings.AccessToken == null)
            {
                settings.AccessToken = Trimmed(settings.LegacyAccessToken) ?? ReadToken(legacyTokenPath);
                if (settings.AccessToken == null)
                {
                    settings.AccessToken = GenerateToken();
                    tokenCreated = true;
                }
                WriteToken(tokenPath, settings.AccessToken);
            }
            if (settings.LegacyAccessToken != null)
            {
                settings.LegacyAccessToken = null;
                dirty = true;
            }

            if (dirty)
            {
                settings.Save(path);
            }
            return settings;
        }

        /// <summary>迁移完成后删除旧版本的数据目录（包括其中的旧日志），返回是否删除了。</summary>
        public static bool DeleteLegacyDirectory(string legacyDirectory)
        {
            if (legacyDirectory == null || !Directory.Exists(legacyDirectory))
            {
                return false;
            }
            Directory.Delete(legacyDirectory, true);
            return true;
        }

        private static AppSettings Read(string path)
        {
            try
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path, Encoding.UTF8), JsonOptions) ?? new AppSettings();
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("配置文件格式错误：" + path, ex);
            }
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }

        public static string GenerateToken()
        {
            byte[] buffer = new byte[24];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(buffer);
            }
            return Convert.ToBase64String(buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string ReadToken(string path)
        {
            return path != null && File.Exists(path) ? Trimmed(File.ReadAllText(path, Encoding.UTF8)) : null;
        }

        private static void WriteToken(string path, string token)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, token, new UTF8Encoding(false));
        }

        private static string Trimmed(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
