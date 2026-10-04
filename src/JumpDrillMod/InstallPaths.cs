using System.IO;
using UnityEngine;

namespace JumpDrillMod
{
    /// <summary>
    /// 動かしているインスタンスの場所。
    /// </summary>
    /// <remarks>
    /// <see cref="Application.dataPath"/> は<b>メインスレッドからしか呼べない</b>。
    /// メダルの集計はリプレイを何十本も解析するので別スレッドで回すが、
    /// そこから触ると例外になる。起動時（メインスレッド）に1度だけ取って持っておく。
    /// </remarks>
    internal static class InstallPaths
    {
        /// <summary><c>&lt;install&gt;\Beat Saber_Data</c>。</summary>
        internal static string DataPath { get; private set; } = string.Empty;

        /// <summary>インストールのルート。</summary>
        internal static string Root { get; private set; } = string.Empty;

        internal static string UserData => Path.Combine(Root, "UserData");

        /// <summary><see cref="Plugin"/> の初期化から呼ぶ。メインスレッドで。</summary>
        internal static void Capture()
        {
            DataPath = Application.dataPath;
            Root = Directory.GetParent(DataPath).FullName;
        }
    }
}
