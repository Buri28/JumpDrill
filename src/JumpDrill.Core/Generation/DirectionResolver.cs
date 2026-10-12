using JumpDrill.Model;

namespace JumpDrill.Generation
{
    /// <summary>循環上の1点をどの向きで切らせるかを決める。</summary>
    public static class DirectionResolver
    {
        /// <summary>
        /// 切る向きは「そのノーツに入ってくる動き」で決まる。
        ///
        /// 8&gt;b の往復なら、8 を叩いたあと b へ向かって動くので、
        /// b は 8→b の向き（右下がり）で切ることになる。戻りは b→8 なので
        /// 8 は左上がりで切る。つまり<b>直前の点から今の点へ向かうベクトル</b>が
        /// そのノーツの矢印になる。次の点へ向かうベクトルではない。
        /// </summary>
        /// <param name="cycleIndex">その手が何回目に叩く点か。0 始まりで単調増加。</param>
        public static CutDirection Resolve(HandSequence sequence, int cycleIndex, DirectionMode mode)
        {
            var step = sequence.StepAt(cycleIndex);
            var previous = sequence.StepAt(cycleIndex - 1);

            switch (mode)
            {
                case DirectionMode.Dot:
                    return CutDirection.Any;

                case DirectionMode.Perpendicular:
                case DirectionMode.PerpendicularOuter:
                    // 移動の向きを常に同じ側へ 90° 回す。行きと戻りで移動の向きが逆になるので、
                    // 矢印も 3>b なら ▶◀ と交互になり、振りは往復のまま移動軸と振り軸だけが直交する。
                    // ノーツごとに回す側まで入れ替えると、反転が二重に掛かって毎回同じ向き（DD）になる。
                    // 回す側は手で逆にする。左右反転した配置（R3b と L2a）で矢印も左右対称になる。
                    // 右手を反時計回りに回すと内回り。外回りはその逆。
                    bool counterClockwise = (sequence.Hand == Hand.Right) != (mode == DirectionMode.PerpendicularOuter);
                    return Geometry.PerpendicularTo(previous.Position, step.Position, counterClockwise, step.Straighten);

                case DirectionMode.Explicit:
                case DirectionMode.Axis:
                default:
                    // 記法で明示されていればそれが最優先。
                    if (step.ExplicitDirection.HasValue)
                        return step.ExplicitDirection.Value;
                    // 配置ベクトルと一致させる。フォアとバックが同一直線に乗る。
                    // s が付いていれば、そこから上下左右に倒す。
                    return Geometry.FromTransition(previous.Position, step.Position, step.Straighten);
            }
        }
    }
}
