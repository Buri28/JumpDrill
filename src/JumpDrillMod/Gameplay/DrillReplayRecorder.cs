using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IPA.Utilities;
using JumpDrill.Model;
using JumpDrill.Replays;
using UnityEngine;
using UnityEngine.XR;
using Zenject;
using ReplayQuaternion = JumpDrill.Replays.Quaternion;
using ReplayVector3 = JumpDrill.Replays.Vector3;

namespace JumpDrillMod.Gameplay
{
    /// <summary>
    /// ドリルを叩いている間の動きを記録し、終わったら BeatLeader と同じ形式のリプレイに書く。
    /// </summary>
    /// <remarks>
    /// 記録の中身と取り方は BeatLeader のリプレイ記録に合わせてある。
    /// 同じ数字が出るように、JumpDrill.Core の解析が読む値は同じところから取る。
    /// <list type="bullet">
    ///   <item>フレーム：毎フレームの終わりに、頭とセイバーの位置と向きを
    ///         プレイ空間の親（<c>PlayerTransforms</c> の原点の親）から見た値で</item>
    ///   <item>ノーツ：本体の採点がそのノーツを取り上げたときの曲の時刻（<see cref="ScoreControllerPatch"/>）と、
    ///         ゲームが渡す切り方の情報。振りの評価（前後の角度）は採点が終わったところで、
    ///         上限 1 で切らない値（<see cref="SwingRatingPatches"/>）を入れる</item>
    ///   <item>開始時刻：この場面ができたとき。ファイル名の末尾になり、
    ///         BeatLeader が残した同じプレイと見分けるのに使う</item>
    /// </list>
    ///
    /// 記録するのはドリル（曲名に ID があるもの）だけ。普通の曲では再現性の数字に意味が無い。
    /// </remarks>
    internal class DrillReplayRecorder : IInitializable, IDisposable, ILateTickable
    {
        private readonly SaberManager saberManager;
        private readonly PlayerTransforms playerTransforms;
        private readonly BeatmapObjectManager beatmapObjectManager;
        private readonly AudioTimeSyncController timeSync;
        private readonly ScoreController scoreController;
        private readonly GameplayCoreSceneSetupData setupData;
        private readonly StandardLevelScenesTransitionSetupDataSO transitionSetup;
        private readonly PauseController? pauseController;
        private readonly VariableMovementDataProvider? movementData;

        private readonly Replay replay = new Replay
        {
            Info = new ReplayInfo(),
            Frames = new List<ReplayFrame>(),
            Notes = new List<ReplayNote>(),
        };

        /// <summary>
        /// 出てきたノーツ。ID と出る時刻は出てきたときに、切り方は切ったときに、
        /// 時刻は採点に取り上げられたときに、種類と振りの評価は採点が終わったときに入れる（BeatLeader と同じ順）。
        /// </summary>
        private readonly Dictionary<NoteData, ReplayNote> byNote = new Dictionary<NoteData, ReplayNote>();

        /// <summary>種類が決まらないまま終わったノーツ。書く前に落とす（BeatLeader と同じ）。</summary>
        private readonly HashSet<ReplayNote> undecided = new HashSet<ReplayNote>();

        private Transform? origin;
        private Transform? head;
        private Transform? leftSaber;
        private Transform? rightSaber;

        private bool paused;
        private bool stopped;

        /// <summary>フレームが取れなくなった。ノーツはそのまま記録し、終わったら書く。</summary>
        private bool framesFailed;

        /// <summary>本体の自動プレイで動いていた。記録は自動プレイの形（追跡系を Unknown）で書く。</summary>
        private bool autoplay;

        /// <summary>
        /// 本体のコントローラー（<c>VRController</c>）と、その自動プレイの印 <c>autoPlayActive</c>。
        /// 印は 1.44 から。それより前の版には無いので、名前で探して無ければ見ない。
        /// </summary>
        private static readonly Type? ControllerType = Type.GetType("VRController, HMLib", false);
        private static readonly System.Reflection.FieldInfo? AutoPlayActive = ControllerType?.GetField("autoPlayActive");
        private UnityEngine.Object[] controllers = Array.Empty<UnityEngine.Object>();
        private bool controllersSearched;

        /// <summary>
        /// いちばん最近の書き出し。叩き終えて画面に戻った側が、書き終わるのを待ってから読み直すのに使う
        /// （書き出しは別スレッドなので、戻った時点ではまだファイルが無いことがある）。
        /// </summary>
        internal static System.Threading.Tasks.Task PendingSave { get; private set; } = System.Threading.Tasks.Task.CompletedTask;

        /// <summary>いちばん最近に書き終えたリプレイ。リーダーボードがいま叩いた記録を選ぶのに使う。</summary>
        internal static string? LastSavedPath { get; private set; }

        /// <summary>
        /// リプレイを書き終えたとき（メインスレッドで呼ぶ）。曲選択の枠は、戻った時点ではまだ
        /// ファイルが無いことがあるので、これを受けて読み直す。
        /// </summary>
        internal static event Action<string>? Saved;

        /// <summary>いま記録している場面。本体の処理に差し込んだところ（Harmony）から呼ぶ先。</summary>
        internal static DrillReplayRecorder? Active { get; private set; }

        /// <summary>ドリルを記録している最中か。差し込んだ処理は、それ以外では何もしない。</summary>
        internal static bool IsRecording => Active != null && !Active.stopped;

        private static readonly IPA.Utilities.FieldAccessor<CutScoreBuffer, SaberSwingRatingCounter>.Accessor SwingCounter =
            IPA.Utilities.FieldAccessor<CutScoreBuffer, SaberSwingRatingCounter>.GetAccessor("_saberSwingRatingCounter");

        internal DrillReplayRecorder(
            SaberManager saberManager,
            PlayerTransforms playerTransforms,
            BeatmapObjectManager beatmapObjectManager,
            AudioTimeSyncController timeSync,
            ScoreController scoreController,
            GameplayCoreSceneSetupData setupData,
            StandardLevelScenesTransitionSetupDataSO transitionSetup,
            [InjectOptional] PauseController? pauseController,
            [InjectOptional] VariableMovementDataProvider? movementData)
        {
            this.saberManager = saberManager;
            this.playerTransforms = playerTransforms;
            this.beatmapObjectManager = beatmapObjectManager;
            this.timeSync = timeSync;
            this.scoreController = scoreController;
            this.setupData = setupData;
            this.transitionSetup = transitionSetup;
            this.pauseController = pauseController;
            this.movementData = movementData;

            // BeatLeader と同じく、この場面ができた時刻（UTC の秒）
            replay.Info.Timestamp = ((long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds).ToString();
        }

        public void Initialize()
        {
            SwingRatingPatches.Clear();
            Active = this;

            beatmapObjectManager.noteWasAddedEvent += OnNoteWasAdded;
            beatmapObjectManager.noteWasCutEvent += OnNoteWasCut;
            scoreController.scoringForNoteFinishedEvent += OnScoringFinished;
            transitionSetup.didFinishEvent += OnLevelFinished;

            if (pauseController != null)
            {
                pauseController.didPauseEvent += OnPause;
                pauseController.didResumeEvent += OnResume;
            }

            Plugin.LogDebug("recording " + setupData.beatmapLevel?.songName);
        }

        public void Dispose()
        {
            if (Active == this) Active = null;
            SwingRatingPatches.Clear();

            beatmapObjectManager.noteWasAddedEvent -= OnNoteWasAdded;
            beatmapObjectManager.noteWasCutEvent -= OnNoteWasCut;
            scoreController.scoringForNoteFinishedEvent -= OnScoringFinished;
            transitionSetup.didFinishEvent -= OnLevelFinished;

            if (pauseController != null)
            {
                pauseController.didPauseEvent -= OnPause;
                pauseController.didResumeEvent -= OnResume;
            }
        }

        // ───────── フレーム ─────────

        public void LateTick()
        {
            if (stopped || paused || framesFailed) return;

            try
            {
                if (head == null) Resolve();
                if (head == null || leftSaber == null || rightSaber == null) return;

                if (!autoplay) autoplay = AutoplayActive();

                replay.Frames.Add(new ReplayFrame
                {
                    Time = timeSync.songTime,
                    Fps = Mathf.RoundToInt(Time.timeScale / Time.deltaTime),
                    Head = Local(head),
                    Left = Local(leftSaber),
                    Right = Local(rightSaber),
                });
            }
            catch (Exception e)
            {
                // 1度落ちたらフレームは諦める。毎フレーム同じ例外を出し続けない。
                // ノーツは取れているので、終わったら書く
                Plugin.Log?.Error("replay frames stopped: " + e);
                framesFailed = true;
            }
        }

        private bool AutoplayActive()
        {
            if (AutoPlayActive == null) return false;
            foreach (var controller in controllers)
            {
                if (controller != null && (bool)AutoPlayActive.GetValue(controller)) return true;
            }
            return false;
        }

        /// <summary>
        /// 位置を取る元を決める。頭と原点の親は <c>PlayerTransforms</c> の中にしか無いので名前で読む。
        /// </summary>
        private void Resolve()
        {
            var transforms = playerTransforms;

            // 全部のオブジェクトを見る重い検索なので1回だけ。頭が取れない間は Resolve が毎フレーム呼ばれる
            if (!controllersSearched && ControllerType != null && AutoPlayActive != null)
            {
                controllersSearched = true;
                controllers = Resources.FindObjectsOfTypeAll(ControllerType);
            }
            head = FieldAccessor<PlayerTransforms, Transform>.Get(ref transforms, "_headTransform");
            origin = FieldAccessor<PlayerTransforms, Transform>.Get(ref transforms, "_originParentTransform");
            leftSaber = saberManager.leftSaber.transform;
            rightSaber = saberManager.rightSaber.transform;
        }

        /// <summary>プレイ空間の親から見た位置と向き。親が無ければ世界の座標のまま。</summary>
        private ReplayTransform Local(Transform target)
        {
            var position = origin != null ? origin.InverseTransformPoint(target.position) : target.position;
            var rotation = origin != null
                ? UnityEngine.Quaternion.Inverse(origin.rotation) * target.rotation
                : target.rotation;

            return new ReplayTransform
            {
                Position = Convert(position),
                Rotation = new ReplayQuaternion { X = rotation.x, Y = rotation.y, Z = rotation.z, W = rotation.w },
            };
        }

        private static ReplayVector3 Convert(UnityEngine.Vector3 v)
        {
            return new ReplayVector3 { X = v.x, Y = v.y, Z = v.z };
        }

        private void OnPause() => paused = true;

        private void OnResume() => paused = false;

        // ───────── ノーツ ─────────

        private void OnNoteWasAdded(NoteData data, NoteSpawnData spawn)
        {
            if (stopped) return;

            byNote[data] = new ReplayNote
            {
                NoteId = NoteId(data),
                SpawnTime = data.time,
            };
        }

        private void OnNoteWasCut(NoteController controller, in global::NoteCutInfo info)
        {
            if (stopped) return;

            ReplayNote note;
            if (info.noteData == null || !byNote.TryGetValue(info.noteData, out note)) return;

            note.Cut = new JumpDrill.Replays.NoteCutInfo
            {
                SpeedOk = info.speedOK,
                DirectionOk = info.directionOK,
                SaberTypeOk = info.saberTypeOK,
                WasCutTooSoon = info.wasCutTooSoon,
                SaberSpeed = info.saberSpeed,
                SaberDirection = Convert(info.saberDir),
                SaberType = (int)info.saberType,
                TimeDeviation = info.timeDeviation,
                CutDirDeviation = info.cutDirDeviation,
                CutPoint = Convert(info.cutPoint),
                CutNormal = Convert(info.cutNormal),
                CutDistanceToCenter = info.cutDistanceToCenter,
                CutAngle = info.cutAngle,
            };
        }

        /// <summary>
        /// 本体の採点がノーツを取り上げる直前（<see cref="ScoreControllerPatch"/> から）。
        /// 取り上げられるノーツに、いまの曲の時刻を入れて記録に並べる。
        /// </summary>
        /// <remarks>
        /// どこまで取り上げるかの判定は本体の <c>ScoreController.LateUpdate</c> と同じ
        /// （BeatLeader の <c>OnBeforeScoreControllerLateUpdate</c> を写したもの）。
        /// 点の無いノーツの取り逃しは並べない。
        /// </remarks>
        internal void OnBeforeScoring(AudioTimeSyncController sync, List<float> noteTimesWithoutElements,
            List<ScoringElement> elements)
        {
            if (stopped || sync == null || elements == null) return;

            try
            {
                float songTime = sync.songTime;
                float firstWaiting = noteTimesWithoutElements != null && noteTimesWithoutElements.Count > 0
                    ? noteTimesWithoutElements[0]
                    : float.MaxValue;
                float horizon = songTime + 0.15f;

                foreach (var element in elements)
                {
                    if (element.time >= horizon && element.time <= firstWaiting) break;

                    var data = element.noteData;
                    if (element is MissScoringElement && data.scoringType == NoteData.ScoringType.NoScore) continue;

                    ReplayNote note;
                    if (!byNote.TryGetValue(data, out note)) continue;

                    note.EventTime = songTime;
                    replay.Notes.Add(note);
                    undecided.Add(note);
                }
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("replay note timing failed: " + e);
            }
        }

        /// <summary>BeatLeader のノーツ ID。配置がそのまま数字に入る。</summary>
        private static int NoteId(NoteData data)
        {
            return ((int)data.scoringType + 2) * 10000
                 + data.lineIndex * 1000
                 + (int)data.noteLineLayer * 100
                 + (int)data.colorType * 10
                 + (int)data.cutDirection;
        }

        private void OnScoringFinished(ScoringElement element)
        {
            if (stopped) return;

            ReplayNote note;
            if (element?.noteData == null || !byNote.TryGetValue(element.noteData, out note)) return;

            bool bomb = element.noteData.colorType == ColorType.None;

            switch (element)
            {
                case GoodCutScoringElement good:
                    note.EventType = NoteEventType.Good;
                    var buffer = good.cutScoreBuffer as CutScoreBuffer;
                    if (note.Cut != null && buffer != null)
                    {
                        var counter = SwingCounter(ref buffer);
                        float before, after;
                        SwingRatingPatches.Take(counter, out before, out after);
                        note.Cut.BeforeCutRating = before;
                        note.Cut.AfterCutRating = after;
                    }
                    undecided.Remove(note);
                    break;

                case BadCutScoringElement _:
                    note.EventType = bomb ? NoteEventType.Bomb : NoteEventType.Bad;
                    undecided.Remove(note);
                    break;

                case MissScoringElement _:
                    // 爆弾を避けたのは取り逃しではない
                    if (!bomb)
                    {
                        note.EventType = NoteEventType.Miss;
                        undecided.Remove(note);
                    }
                    break;
            }
        }

        // ───────── 終わり ─────────

        private void OnLevelFinished(StandardLevelScenesTransitionSetupDataSO data, LevelCompletionResults results)
        {
            if (stopped) return;
            stopped = true;

            try
            {
                replay.Notes.RemoveAll(undecided.Contains);
                if (replay.Notes.Count == 0) return;

                FillInfo(results);

                // 名前の印は BeatLeader と同じ付け方。練習は練習の印だけで、途中でやめても exit は付けない
                bool exited = replay.Info.Speed == 0f
                           && replay.Info.FailTime == 0f
                           && (results.levelEndAction == LevelCompletionResults.LevelEndAction.Quit
                               || results.levelEndAction == LevelCompletionResults.LevelEndAction.Restart);

                string folder = Path.Combine(Path.Combine(InstallPaths.UserData, ReplayLibrary.OwnReplayMod), "Replays");
                string path = Path.Combine(folder, ReplayLibrary.OwnFileName(replay.Info, exited));

                // 書くのは別スレッドで。数 MB になるので、結果画面への切り替えを止めない
                PendingSave = Task.Run(() => Save(folder, path));
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not finish the replay: " + e);
            }
        }

        private void Save(string folder, string path)
        {
            try
            {
                Directory.CreateDirectory(folder);

                // 書きかけのファイルを読まれないように、別名で書いてから置き換える
                string temp = path + ".tmp";
                ReplayWriter.Write(replay, temp);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);

                Plugin.LogDebug("replay saved: " + Path.GetFileName(path));

                LastSavedPath = path;
                Task.Factory.StartNew(() => Saved?.Invoke(path), System.Threading.CancellationToken.None,
                    TaskCreationOptions.None, IPA.Utilities.Async.UnityMainThreadTaskScheduler.Default);
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not save the replay: " + e);
            }
        }

        private void FillInfo(LevelCompletionResults results)
        {
            var info = replay.Info;
            var level = setupData.beatmapLevel;
            var key = setupData.beatmapKey;
            var player = setupData.playerSpecificSettings;
            var practice = setupData.practiceSettings;

            info.Version = "JumpDrillMod " + typeof(Plugin).Assembly.GetName().Version;
            info.GameVersion = Application.version;
            info.PlayerId = ReplayLibrary.OwnPlayerId;
            info.PlayerName = string.Empty;
            info.Platform = string.Empty;

            // 3つとも Unknown だと、解析は自動プレイとみなして記録から外す（BeatLeader の書き方に合わせた判定）
            // 本体の自動プレイ（1.44 から）は、機器がつながったままでも自動プレイとして書く
            info.TrackingSystem = autoplay ? "Unknown" : Known(XRSettings.loadedDeviceName);
            info.Hmd = autoplay ? "Unknown" : DeviceName(InputDeviceCharacteristics.HeadMounted);
            info.Controller = autoplay ? "Unknown" : DeviceName(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right);

            info.Hash = key.levelId?.Replace("custom_level_", "") ?? string.Empty;
            info.SongName = level?.songName ?? string.Empty;
            info.Mapper = level?.allMappers == null ? string.Empty : string.Join(",", level.allMappers);
            info.Difficulty = key.difficulty.ToString();
            info.Mode = key.beatmapCharacteristic?.serializedName ?? string.Empty;
            info.Environment = setupData.targetEnvironmentInfo?.serializedName ?? string.Empty;
            info.Modifiers = Modifiers(setupData.gameplayModifiers);
            info.Score = results.multipliedScore;
            info.JumpDistance = movementData?.jumpDistance ?? 0f;
            info.LeftHanded = player?.leftHanded ?? false;
            info.Height = player == null || player.automaticPlayerHeight ? 0f : player.playerHeight;

            // 練習の設定がある＝練習モード。BeatLeader は速さが 0 でないかで練習かを見分ける
            info.StartTime = practice?.startSongTime ?? 0f;
            info.Speed = practice?.songSpeedMul ?? 0f;

            info.FailTime = results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Failed
                ? timeSync.songTime
                : 0f;
        }

        private static string Known(string? value)
        {
            return string.IsNullOrEmpty(value) ? "Unknown" : value!;
        }

        private static string DeviceName(InputDeviceCharacteristics characteristics)
        {
            try
            {
                var devices = new List<InputDevice>();
                InputDevices.GetDevicesWithCharacteristics(characteristics, devices);
                return Known(devices.Select(d => d.name).FirstOrDefault(n => !string.IsNullOrEmpty(n)));
            }
            catch (Exception)
            {
                return "Unknown";
            }
        }

        /// <summary>BeatLeader と同じ略号。解析では使わないが、同じ形で残しておく。</summary>
        private static string Modifiers(GameplayModifiers? m)
        {
            if (m == null) return string.Empty;

            var list = new List<string>();
            if (m.disappearingArrows) list.Add("DA");
            if (m.songSpeed == GameplayModifiers.SongSpeed.Faster) list.Add("FS");
            if (m.songSpeed == GameplayModifiers.SongSpeed.Slower) list.Add("SS");
            if (m.songSpeed == GameplayModifiers.SongSpeed.SuperFast) list.Add("SF");
            if (m.ghostNotes) list.Add("GN");
            if (m.noArrows) list.Add("NA");
            if (m.noBombs) list.Add("NB");
            if (m.noFailOn0Energy) list.Add("NF");
            if (m.enabledObstacleType == GameplayModifiers.EnabledObstacleType.NoObstacles) list.Add("NO");
            if (m.proMode) list.Add("PM");
            if (m.smallCubes) list.Add("SC");
            if (m.instaFail) list.Add("IF");
            if (m.energyType == GameplayModifiers.EnergyType.Battery) list.Add("BE");
            if (m.strictAngles) list.Add("SA");
            return string.Join(",", list);
        }
    }
}
