using Newtonsoft.Json;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PCDeviceManage
{
    /// <summary>
    /// 用户配置，保存在 %LocalAppData%\PCManage\settings.json。
    /// 程序目录可能没有写权限，访问令牌也不应该和程序放在一起，所以不再使用 exe.config。
    /// </summary>
    public sealed class AppSettings
    {
        public const int DefaultWebPort = 9527;
        public const string DefaultWebHost = "127.0.0.1";

        [JsonProperty("webHost")]
        public string WebHost { get; set; } = DefaultWebHost;

        [JsonProperty("webPort")]
        public int WebPort { get; set; } = DefaultWebPort;

        [JsonProperty("accessToken")]
        public string AccessToken { get; set; }

        /// <summary>frp\frpc.exe 和 frp\frpc.ini 都存在时是否启动 frp。</summary>
        [JsonProperty("frpEnabled")]
        public bool FrpEnabled { get; set; } = true;

        public static string DataDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCManage");

        public static string DefaultPath => Path.Combine(DataDirectory, "settings.json");

        /// <summary>P0 版本保存访问令牌的位置，首次加载时迁移进 settings.json 后删除。</summary>
        public static string LegacyTokenPath => Path.Combine(DataDirectory, "access_token");

        /// <summary>
        /// 读取配置；文件不存在时从 P0 版本的 access_token 文件迁移令牌，没有令牌则生成一个。
        /// 文件存在但无法解析时抛出异常，而不是悄悄生成新令牌覆盖用户配置。
        /// </summary>
        public static AppSettings LoadOrCreate(string path, string legacyTokenPath, out bool tokenCreated)
        {
            tokenCreated = false;
            bool dirty = false;
            AppSettings settings;

            if (File.Exists(path))
            {
                try
                {
                    settings = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(path, Encoding.UTF8)) ?? new AppSettings();
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException("配置文件格式错误：" + path, ex);
                }
            }
            else
            {
                settings = new AppSettings();
                settings.MigrateLegacyToken(legacyTokenPath);
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
            if (string.IsNullOrWhiteSpace(settings.AccessToken))
            {
                settings.AccessToken = GenerateToken();
                tokenCreated = true;
                dirty = true;
            }

            if (dirty)
            {
                settings.Save(path);
            }

            if (legacyTokenPath != null && File.Exists(legacyTokenPath))
            {
                File.Delete(legacyTokenPath);
            }
            return settings;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(this, Formatting.Indented), new UTF8Encoding(false));
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

        private void MigrateLegacyToken(string legacyTokenPath)
        {
            if (legacyTokenPath != null && File.Exists(legacyTokenPath))
            {
                AccessToken = File.ReadAllText(legacyTokenPath, Encoding.UTF8).Trim();
            }
        }
    }
}
