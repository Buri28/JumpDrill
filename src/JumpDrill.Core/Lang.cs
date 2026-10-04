namespace JumpDrill
{
    /// <summary>
    /// 画面に出す文言の言語。GUI が起動時に決める。CLI と MOD は日本語のまま。
    ///
    /// 文言は呼び出し側に日本語と英語を並べて書く。訳を別ファイルに分けると、
    /// どの文がどこに出るのかが追えなくなるため。
    /// </summary>
    public static class Lang
    {
        public static bool English { get; set; }

        public static string T(string japanese, string english)
        {
            return English ? english : japanese;
        }
    }
}
