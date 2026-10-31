using System.IO;

namespace SkyLight.Configuration
{
    internal static class SafeFile
    {
        /// <summary>
        /// 一時ファイルに書いてから置き換える。書き込み中にゲームが落ちても、元のファイルは壊れない
        /// （直接上書きだと、途中で切れた JSON が残って、次から読めなくなる）。
        /// </summary>
        public static void WriteAllText(string path, string text)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }
}
