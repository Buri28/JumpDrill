using System;
using System.Collections.Generic;

namespace JumpDrill.Output
{
    /// <summary>Info.dat（v2 形式、キーが <c>_</c> で始まるもの）の難易度の並び。</summary>
    internal static class InfoDat
    {
        /// <summary>難易度ごとに、モード（<c>_beatmapCharacteristicName</c>）と難易度の中身を、書かれた順に返す。</summary>
        public static IEnumerable<KeyValuePair<string, Dictionary<string, object>>> Difficulties(Dictionary<string, object> root)
        {
            foreach (var set in ListOf(root, "_difficultyBeatmapSets"))
            {
                object name;
                string mode = set.TryGetValue("_beatmapCharacteristicName", out name) ? name as string : null;
                foreach (var map in ListOf(set, "_difficultyBeatmaps"))
                    yield return new KeyValuePair<string, Dictionary<string, object>>(mode, map);
            }
        }

        private static IEnumerable<Dictionary<string, object>> ListOf(Dictionary<string, object> parent, string key)
        {
            object value;
            var list = parent != null && parent.TryGetValue(key, out value) ? value as List<object> : null;
            if (list == null) yield break;
            foreach (var item in list)
            {
                var dict = item as Dictionary<string, object>;
                if (dict != null) yield return dict;
            }
        }
    }
}
