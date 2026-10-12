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

        /// <summary>
        /// Lv2: 配置ベクトルに垂直。移動軸と振り軸が直交する。<b>内回り。</b>
        /// 切る順に矢印をつなぐと手は円を描き、その上側が内側へ回る
        /// （右手は反時計回り、左手は時計回り）。右手 3&gt;b なら b ▶、3 ◀。横の 1&gt;4 なら 4 ▲、1 ▼。
        /// </summary>
        Perpendicular,

        /// <summary>Lv3: ドット。方向の制約を外して軌道だけを見る。</summary>
        Dot,

        /// <summary>遷移ごとに記法内で明示（<c>8@dl</c> のように書く）。</summary>
        Explicit,

        /// <summary>
        /// <see cref="Perpendicular"/> の回る向きを逆にしたもの。<b>外回り</b>（円の上側が外側へ回る）。
        /// 右手 3&gt;b なら b ◀、3 ▶。
        /// 値は末尾に足す（GUI は選択肢の位置をそのまま設定に残している）。
        /// </summary>
        PerpendicularOuter,
    }
}
