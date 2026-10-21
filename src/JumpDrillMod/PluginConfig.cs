namespace JumpDrillMod
{
    /// <summary>
    /// 生成の指定を保存する。IPA.Config が UserData\JumpDrillMod.json に自動で読み書きする。
    /// </summary>
    /// <remarks>
    /// 指定項目は JumpDrill.Core の <c>DrillOptions</c> と対応させてある。
    /// 検証と既定値は JumpDrill.Core 側が持っているので、ここでは保存だけを担当して
    /// 解釈は <see cref="Services.DrillGenerationService"/> から JumpDrill.Core に渡す。
    /// </remarks>
    public class PluginConfig
    {
        public static PluginConfig? Instance { get; set; }

        /// <summary>
        /// 遷移。JumpDrill.Core の記法をそのまま入れる（例: <c>R8b</c>、<c>L4sbs</c>）。
        /// 手の接頭辞と <c>s</c> もここに含まれる。
        /// </summary>
        public virtual string Sequence { get; set; } = "R8b";

        /// <summary>
        /// 反転してもう片方の手にも同じ遷移を置くか（CLI の <c>--mirror</c>）。
        /// 既定は有効（片手ぶん組めば両手のドリルになる）。
        /// </summary>
        public virtual bool Mirror { get; set; } = true;

        /// <summary>
        /// ノーツ間隔を表す BPM。1拍に2ノーツ（1/2）で数える。
        /// </summary>
        /// <remarks>
        /// ms ではなく BPM で持つ。Beat Saber で速さを言うときの単位が BPM のため。
        ///
        /// BPM だけでは間隔が決まらない（同じ 300 BPM が 1/1 と 1/2 で倍違う）ので、
        /// <b>1/2 に固定する</b>（JumpDrill.Core の <c>NotesPerBeat</c> 参照）。
        /// 1/2 なら BPM がそのまま EBPM と同じ値になるので、
        /// ScoreSaber の基準と突き合わせるときも読み替えが要らない。
        /// 1/4 で叩きたければ BPM を倍にすれば間隔は同じ。
        /// </remarks>
        public virtual float IntervalBpm { get; set; } = 150f;

        /// <summary>NJS。間隔ともジャンプ距離とも独立に指定する。</summary>
        public virtual float Njs { get; set; } = 16f;

        /// <summary>ジャンプ距離 (m)。JumpDrill.Coreが <c>_noteJumpStartBeatOffset</c> を逆算する。</summary>
        public virtual float JumpDistance { get; set; } = 18f;

        /// <summary>ドリル本体の尺 (秒)。カウントインとテールは含まない。</summary>
        public virtual float DurationSeconds { get; set; } = 10f;

        /// <summary>セットの繰り返し回数。</summary>
        public virtual int Sets { get; set; } = 1;

        /// <summary>間隔を詰める (%)。0 で無し（CLI の <c>--tight</c>）。</summary>
        public virtual int FlickPercent { get; set; }

        /// <summary>切る方向の指定。JumpDrill.Core の <c>DirectionMode</c> の名前をそのまま入れる。</summary>
        public virtual string Direction { get; set; } = "Axis";

        /// <summary>一直線のジャンプで2点の間も埋める（CLI の <c>--tower</c>）。</summary>
        public virtual bool Tower { get; set; }

        /// <summary>
        /// 複数手のときの噛み合わせ。JumpDrill.Core の <c>HandPattern</c> の名前をそのまま入れる。
        /// 片手だけのときは効かない。
        /// </summary>
        public virtual string HandPattern { get; set; } = "Split";

        /// <summary>クリックの粒度。JumpDrill.Core の <c>ClickMode</c> の名前をそのまま入れる。</summary>
        public virtual string Click { get; set; } = "Down";

        /// <summary>カウントインのクリック数。クリック周期で先頭に並ぶ。</summary>
        public virtual int CountInClicks { get; set; } = 8;

        /// <summary>
        /// 曲一覧に出す専用パックの名前。SongCore の folders.xml に
        /// <c>Pack=2</c> / <c>WIP=True</c> で登録し、フォルダはインストールのルート直下に作る。
        /// </summary>
        /// <remarks>
        /// JumpDrill の CLI の <c>--register-pack</c> と同じ名前にしておけば、
        /// CLI で作ったドリルと同じパックに並ぶ。
        /// </remarks>
        public virtual string PackName { get; set; } = "JumpDrill";

        /// <summary>
        /// 軌道の図で平均軌道だけを描くか（GUI の「平均だけ」）。
        /// 叩くたびに切り替え直さなくて済むように残しておく。
        /// </summary>
        public virtual bool DiagramMeanOnly { get; set; } = false;

        /// <summary>
        /// 軌道の図でフォアだけ（"fore"）かバックだけ（"back"）を出すか。両方なら空（GUI の「フォアとバック」）。
        /// タワー・スタックでは行きと帰りが重なるので、片方ずつ見られるようにする。
        /// </summary>
        public virtual string DiagramStroke { get; set; } = string.Empty;

        /// <summary>
        /// 軌道の図と空間の振りで、ノーツを良い切りの当たり判定の大きさで描くか（GUI の「当たり判定の大きさ」）。
        /// false なら見た目の大きさ。
        /// </summary>
        public virtual bool DiagramHitbox { get; set; } = false;

        /// <summary>
        /// 選んでいる記録の振りを、メニューの空間に実物大で出すか（GUI の 3D 表示の VR 版）。
        /// </summary>
        public virtual bool DiagramRoom { get; set; } = false;

        /// <summary>
        /// 空間に出す振りを、プレイしたときの位置からどれだけ前へずらすか (m)。
        /// そのままだと振りの中に立つ形になり、全体を見渡しにくい。
        /// </summary>
        public virtual float DiagramRoomForward { get; set; } = 0f;

        /// <summary>
        /// リーダーボードを新しい順に並べるか。false なら順位（<see cref="RankByAccuracy"/> の数字の高い順）。
        /// 叩くたびに切り替え直さなくて済むように残しておく。
        /// </summary>
        public virtual bool LeaderboardNewestFirst { get; set; } = false;

        /// <summary>
        /// 順位・ベスト・メダルを ACC（精度 %）で決めるか。false なら REPRO（再現精度 %）。
        /// Jump Drill 画面とリーダーボードで共通（<see cref="Services.RankSetting"/>）。
        /// </summary>
        public virtual bool RankByAccuracy { get; set; } = false;

        /// <summary>
        /// Jump Drill 画面で最後に見ていたステージ。空は Standard、<c>other</c> は Other、
        /// <c>custom:名前</c> はカスタムステージ（中身は GUI と同じ <c>UserData\JumpDrill\Stages</c> のファイル）。
        /// </summary>
        public virtual string MedalStage { get; set; } = string.Empty;

        /// <summary>リプレイの整理で、ドリルごとに残す記録の数（REPRO・ACC それぞれの順位の上位）。10 以上。</summary>
        public virtual int ReplayKeep { get; set; } = 20;

        /// <summary>
        /// ドリルを叩くたびに、そのドリルの上位 <see cref="ReplayKeep"/> 件より下の記録を消すか。既定はしない。
        /// </summary>
        public virtual bool ReplayAutoDelete { get; set; } = false;

        /// <summary>
        /// 生成のたびに同じ譜面フォルダを上書きするか。
        /// true だと作り直しても譜面フォルダが増えない。
        /// </summary>
        /// <remarks>
        /// 譜面名には間隔や尺が入る（JumpDrill.Core の <c>DrillNaming</c>）ので、
        /// 指定を変えれば別フォルダになる。上書きが効くのは同じ名前になるときだけ
        /// （クリック音・NJS など名前に入らないものだけを変えたときも含む）。
        /// false なら既にあるときは書かずに知らせる。
        /// </remarks>
        public virtual bool Overwrite { get; set; } = true;

        /// <summary>
        /// ドリルを叩いたときにリプレイを自分でも残すか。
        /// BeatLeader が無い、または練習のリプレイを保存しない設定でも記録が残る。
        /// BeatLeader が同じプレイを残していれば、読むときにそちらを採る。
        /// </summary>
        public virtual bool RecordReplays { get; set; } = true;

        /// <summary>
        /// ドリル以外の曲で最後に見ていた、曲選択のリーダーボード（LeaderboardCore の ID）。
        /// ドリルを選ぶと JumpDrill のものに切り替えるので、ドリルから離れたときにここへ戻す。
        /// 空文字は本体の枠、null はまだ分からない。
        /// </summary>
        public virtual string? LeaderboardOutsideDrills { get; set; } = null;
    }
}
