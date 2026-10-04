using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace JumpDrillMod.Gameplay
{
    /// <summary>
    /// 振りの評価（前後の角度）を、上限 1 で切らずに数え直す。BeatLeader のリプレイと同じ値にするため。
    /// </summary>
    /// <remarks>
    /// 本体の <c>SaberSwingRatingCounter</c> は評価を 1（100%）で頭打ちにする。
    /// BeatLeader は同じ計算を横でもう一度行って振り過ぎのぶんも残し、本体の値が 1 に達していたら
    /// 大きい方を記録する。JumpDrill.Core の解析はその値（PRE / POST）をそのまま平均するので、
    /// 同じにしないと、このMODのリプレイだけ PRE / POST が 100% で止まる。
    ///
    /// 計算は BeatLeader の <c>PreSwingRatingEnhancerPatch</c> / <c>PostSwingRatingEnhancerPatch</c> と
    /// <c>SwingRatingEnhancer</c> を写したもの。BeatLeader が入っていても、こちらは自分の表で数えるので干渉しない。
    /// </remarks>
    internal static class SwingRatingPatches
    {
        internal sealed class Pre
        {
            public float Value;
        }

        internal sealed class Post
        {
            public bool AlreadyCut;
            public float Value;
        }

        /// <summary>振りの動きの記録ごとの、切る前の評価（上限なし）。</summary>
        internal static readonly Dictionary<ISaberMovementData, Pre> PreByMovement = new Dictionary<ISaberMovementData, Pre>();

        /// <summary>評価の数え役ごとの、切った後の評価（上限なし）。</summary>
        internal static readonly Dictionary<SaberSwingRatingCounter, Post> PostByCounter = new Dictionary<SaberSwingRatingCounter, Post>();

        private static readonly AccessTools.FieldRef<SaberSwingRatingCounter, ISaberMovementData> MovementData =
            AccessTools.FieldRefAccess<SaberSwingRatingCounter, ISaberMovementData>("_saberMovementData");
        private static readonly AccessTools.FieldRef<SaberSwingRatingCounter, bool> NotePlaneWasCut =
            AccessTools.FieldRefAccess<SaberSwingRatingCounter, bool>("_notePlaneWasCut");
        private static readonly AccessTools.FieldRef<SaberSwingRatingCounter, bool> RateAfterCut =
            AccessTools.FieldRefAccess<SaberSwingRatingCounter, bool>("_rateAfterCut");
        private static readonly AccessTools.FieldRef<SaberSwingRatingCounter, Plane> NotePlane =
            AccessTools.FieldRefAccess<SaberSwingRatingCounter, Plane>("_notePlane");
        private static readonly AccessTools.FieldRef<SaberSwingRatingCounter, Vector3> CutTopPos =
            AccessTools.FieldRefAccess<SaberSwingRatingCounter, Vector3>("_cutTopPos");
        private static readonly AccessTools.FieldRef<SaberSwingRatingCounter, Vector3> CutBottomPos =
            AccessTools.FieldRefAccess<SaberSwingRatingCounter, Vector3>("_cutBottomPos");
        private static readonly AccessTools.FieldRef<SaberSwingRatingCounter, Vector3> AfterCutTopPos =
            AccessTools.FieldRefAccess<SaberSwingRatingCounter, Vector3>("_afterCutTopPos");
        private static readonly AccessTools.FieldRef<SaberSwingRatingCounter, Vector3> AfterCutBottomPos =
            AccessTools.FieldRefAccess<SaberSwingRatingCounter, Vector3>("_afterCutBottomPos");
        private static readonly AccessTools.FieldRef<SaberSwingRatingCounter, Vector3> CutPlaneNormal =
            AccessTools.FieldRefAccess<SaberSwingRatingCounter, Vector3>("_cutPlaneNormal");

        private static readonly AccessTools.FieldRef<SaberMovementData, BladeMovementDataElement[]> Data =
            AccessTools.FieldRefAccess<SaberMovementData, BladeMovementDataElement[]>("_data");
        private static readonly AccessTools.FieldRef<SaberMovementData, int> NextAddIndex =
            AccessTools.FieldRefAccess<SaberMovementData, int>("_nextAddIndex");
        private static readonly AccessTools.FieldRef<SaberMovementData, int> ValidCount =
            AccessTools.FieldRefAccess<SaberMovementData, int>("_validCount");

        /// <summary>
        /// そのノーツの評価を上限なしで返し、表から外す。採点が終わったところで1度だけ呼ぶ。
        /// </summary>
        internal static void Take(SaberSwingRatingCounter counter, out float before, out float after)
        {
            var movement = MovementData(counter);

            before = counter.beforeCutRating;
            Pre pre;
            if (movement != null && PreByMovement.TryGetValue(movement, out pre)) before = Choose(before, pre.Value);

            after = counter.afterCutRating;
            Post post;
            if (PostByCounter.TryGetValue(counter, out post)) after = Choose(after, post.Value);

            if (movement != null) PreByMovement.Remove(movement);
            PostByCounter.Remove(counter);
        }

        /// <summary>本体の値が 1 に届いているときだけ、上限なしの値を採る（BeatLeader と同じ）。</summary>
        private static float Choose(float real, float unclamped)
        {
            return real < 1f ? real : Mathf.Max(real, unclamped);
        }

        /// <summary>場面が終わったら表を空にする。数え役は使い回されるので、前の曲の値を持ち越さない。</summary>
        internal static void Clear()
        {
            PreByMovement.Clear();
            PostByCounter.Clear();
        }

        [HarmonyPatch(typeof(SaberMovementData), "ComputeSwingRating", new[] { typeof(bool), typeof(float) })]
        internal static class BeforeCut
        {
            private static void Postfix(SaberMovementData __instance, bool overrideSegmenAngle, float overrideValue)
            {
                // ドリルを記録していないときは数えない（表が増え続けないように）
                if (!DrillReplayRecorder.IsRecording) return;

                var data = Data(__instance);
                int validCount = ValidCount(__instance);
                int length = data.Length;

                int index = NextAddIndex(__instance) - 1;
                if (index < 0) index += length;

                float time = data[index].time;
                float earliest = time;
                var normal = data[index].segmentNormal;
                float rating = SaberSwingRating.BeforeCutStepRating(
                    overrideSegmenAngle ? overrideValue : data[index].segmentAngle, 0f);

                for (int count = 2; time - earliest < 0.4 && count < validCount; count++)
                {
                    index--;
                    if (index < 0) index += length;

                    float normalDiff = Vector3.Angle(data[index].segmentNormal, normal);
                    if (normalDiff > 90f) break;

                    rating += SaberSwingRating.BeforeCutStepRating(data[index].segmentAngle, normalDiff);
                    earliest = data[index].time;
                }

                Pre pre;
                if (!PreByMovement.TryGetValue(__instance, out pre)) PreByMovement[__instance] = pre = new Pre();
                pre.Value = rating;
            }
        }

        [HarmonyPatch(typeof(SaberSwingRatingCounter), nameof(SaberSwingRatingCounter.ProcessNewData))]
        internal static class AfterCut
        {
            private static void Prefix(SaberSwingRatingCounter __instance)
            {
                if (!DrillReplayRecorder.IsRecording) return;

                Post post;
                if (!PostByCounter.TryGetValue(__instance, out post)) PostByCounter[__instance] = post = new Post();
                post.AlreadyCut = NotePlaneWasCut(__instance);
            }

            private static void Postfix(SaberSwingRatingCounter __instance, BladeMovementDataElement newData,
                BladeMovementDataElement prevData)
            {
                Post post;
                if (!PostByCounter.TryGetValue(__instance, out post)) return;

                bool rate = RateAfterCut(__instance);

                if (!post.AlreadyCut && !NotePlane(__instance).SameSide(newData.topPos, prevData.topPos))
                {
                    float angleDiff = Vector3.Angle(
                        CutTopPos(__instance) - CutBottomPos(__instance),
                        AfterCutTopPos(__instance) - AfterCutBottomPos(__instance));
                    if (rate) post.Value = SaberSwingRating.AfterCutStepRating(angleDiff, 0f);
                }
                else
                {
                    float normalDiff = Vector3.Angle(newData.segmentNormal, CutPlaneNormal(__instance));
                    if (rate) post.Value += SaberSwingRating.AfterCutStepRating(newData.segmentAngle, normalDiff);
                }
            }
        }
    }
}
