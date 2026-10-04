namespace JumpDrill.Model
{
    /// <summary>
    /// 切る方向の決め方（設計メモ §2「切る方向の設計」）。
    /// 配置ベクトルと切る方向を一致させると、フォアとバックが
    /// 同一直線上を通ることが幾何学的に強制される。
    /// </summary>
    public enum DirectionMode
    {
        /// <summary>Lv1: 配置ベクトルと一致。一直線の往復。</summary>
        Axis,

        /// <summary>Lv2: 配置ベクトルに垂直。移動軸と振り軸が直交する。</summary>
        Perpendicular,

        /// <summary>Lv3: ドット。方向の制約を外して軌道だけを見る。</summary>
        Dot,

        /// <summary>遷移ごとに記法内で明示（<c>8@dl</c> のように書く）。</summary>
        Explicit,
    }
}
