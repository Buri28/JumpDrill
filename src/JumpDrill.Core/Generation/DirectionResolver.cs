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
                    // 交互に逆側の垂直方向を取ることで、振り自体は往復のまま
                    // 移動軸と振り軸だけが直交する。
                    return Geometry.PerpendicularTo(previous.Position, step.Position, cycleIndex % 2 == 0, step.Straighten);

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
