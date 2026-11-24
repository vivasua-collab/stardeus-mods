using System;
using System.Collections;
using System.Collections.Generic;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.UI;
using KL.Utils;
using UnityEngine;

namespace Game.Input {
    public sealed class CameraDolly {
        private GameState S;
        public static bool HasMotion;
        public Vector2Int IdxFromTo;
        private Vector2 startPos;
        private Vector2 endPos;
        private float startOrtho;
        private float endOrtho;
        private Vector2 pos;
        private float ortho;
        private Action afterMotion;
        public AnimationCurve MoveSpeedCurve = AnimationCurve.Constant(0, 1, 1);
        private CameraControls controls;
        public float MoveSpeed;

        public AnimationCurve XCurve;
        public AnimationCurve YCurve;
        public AnimationCurve ZCurve;
        public float Progress;
        public bool IsPaused;
        public bool IsLoop;
        private int pointCount;
        public int NumPoints;

        public void TogglePause() {
            IsPaused = !IsPaused;
        }

        private void SaveCurve(AnimationCurve curve, string key) {
            var keys = curve.keys;
            var k = $"dolly.curve.{key}";
            S.Prefs.SetInt($"{k}.count", keys.Length);
            S.Prefs.SetInt($"{k}.prewrap", (int) curve.preWrapMode);
            S.Prefs.SetInt($"{k}.postwrap", (int) curve.postWrapMode);
            for (int i = 0; i < keys.Length; i++) {
                var kf = keys[i];
                S.Prefs.SetFloat($"{k}.{i}.inT", kf.inTangent);
                S.Prefs.SetFloat($"{k}.{i}.outT", kf.outTangent);
                S.Prefs.SetFloat($"{k}.{i}.inW", kf.inWeight);
                S.Prefs.SetFloat($"{k}.{i}.outW", kf.outWeight);
                S.Prefs.SetFloat($"{k}.{i}.time", kf.time);
                S.Prefs.SetFloat($"{k}.{i}.value", kf.value);
                S.Prefs.SetInt($"{k}.{i}.wmode", (int) kf.weightedMode);
            }
        }

        private AnimationCurve LoadCurve(string key) {
            var k = $"dolly.curve.{key}";
            var curve = new AnimationCurve();
            var count = S.Prefs.GetIntAutoHash($"{k}.count", 0);
            if (count == 0) {
                return curve;
            }
            Keyframe[] kfs = new Keyframe[count];
            for (int i = 0; i < count; i++) {
                var kf = new Keyframe();
                kf.inTangent = S.Prefs.GetFloatAutoHash($"{k}.{i}.inT", 0);
                kf.outTangent = S.Prefs.GetFloatAutoHash($"{k}.{i}.outT", 0);
                kf.inWeight = S.Prefs.GetFloatAutoHash($"{k}.{i}.inW", 0);
                kf.outWeight = S.Prefs.GetFloatAutoHash($"{k}.{i}.outW", 0);
                kf.time = S.Prefs.GetFloatAutoHash($"{k}.{i}.time", 0);
                kf.value = S.Prefs.GetFloatAutoHash($"{k}.{i}.value", 0);
                kf.weightedMode = (WeightedMode) S.Prefs.GetIntAutoHash($"{k}.{i}.wmode", 0);
                kfs[i] = kf;
            }
            curve.preWrapMode = (WrapMode) S.Prefs.GetIntAutoHash($"{k}.prewrap", 0);
            curve.postWrapMode = (WrapMode) S.Prefs.GetIntAutoHash($"{k}.postwrap", 0);
            curve.keys = kfs;
            return curve;
        }

        public void Save(bool saveGame) {
            S.Prefs.SetInt("IdxFrom", IdxFromTo[0]);
            S.Prefs.SetInt("IdxTo", IdxFromTo[1]);
            S.Prefs.SetBool("IsLoop", IsLoop);
            S.Prefs.SetInt("Points", pointCount);
            S.Prefs.SetInt("NumPoints", NumPoints);
            S.Prefs.SetFloat("MoveSpeed", MoveSpeed);
            SaveCurve(MoveSpeedCurve, "MoveSpeed");
            SaveCurve(XCurve, "XCurve");
            SaveCurve(YCurve, "YCurve");
            SaveCurve(ZCurve, "ZCurve");
            if (saveGame) {
                S.CmdQ.Enqueue(new CmdSaveGame(S, S.SaveName, true, false));
            }
        }
        public void Load() {
            var idxFromTo = new Vector2Int();
            idxFromTo[0] = S.Prefs.GetIntAutoHash("IdxFrom", 0);
            idxFromTo[1] = S.Prefs.GetIntAutoHash("IdxTo", 0);
            IdxFromTo = idxFromTo;
            IsLoop = S.Prefs.GetBoolAutoHash("IsLoop", false);
            pointCount = S.Prefs.GetIntAutoHash("Points", 0);
            MoveSpeed = S.Prefs.GetFloatAutoHash("MoveSpeed", 0);
            MoveSpeedCurve = LoadCurve("MoveSpeed");
            NumPoints = S.Prefs.GetIntAutoHash("NumPoints", 10);
            XCurve = LoadCurve("XCurve");
            YCurve = LoadCurve("YCurve");
            ZCurve = LoadCurve("ZCurve");
        }

        public CameraDolly(GameState s, CameraControls controls) {
            HasMotion = false;
            S = s;
            this.controls = controls;
            Subscribe();
        }

        public void Subscribe() {
            S.Sig.CameraDolly.AddListener(OnCameraDolly);
        }

        public void Unsubscribe() {
            HasMotion = false;
            S?.Sig?.CameraDolly.RemoveListener(OnCameraDolly);
        }

        public void Delete(int i) {
            S.Prefs.UnsetFloat($"{Pref.CameraMemoryPos}.{i}.x");
            S.Prefs.UnsetFloat($"{Pref.CameraMemoryPos}.{i}.y");
            S.Prefs.UnsetFloat($"{Pref.CameraMemoryZoom}.{i}");
        }

        public Vector2 Pos(int i) {
            var x = S.Prefs.GetFloatAutoHash($"{Pref.CameraMemoryPos}.{i}.x", 0);
            var y = S.Prefs.GetFloatAutoHash($"{Pref.CameraMemoryPos}.{i}.y", 0);
            return new(x, y);
        }

        public void SetPos(int i, Vector2 value) {
            S.Prefs.SetFloat($"{Pref.CameraMemoryPos}.{i}.x", value.x);
            S.Prefs.SetFloat($"{Pref.CameraMemoryPos}.{i}.y", value.y);
        }
        public float Ortho(int i) {
            return S.Prefs.GetFloatAutoHash($"{Pref.CameraMemoryZoom}.{i}", 0);
        }

        public void SetOrtho(int i, float value) {
            S.Prefs.SetFloat($"{Pref.CameraMemoryZoom}.{i}", value);
        }

        public void SaveCurrentTo(int i) {
            SetPos(i, controls.PrecisePosition);
            SetOrtho(i, controls.PreciseOrtho);
        }

        public void Stop() {
            HasMotion = false;
            controls.WantPosition = controls.PrecisePosition;
            controls.WantOrtho = controls.PreciseOrtho;
        }

        public void UpdateDollyMotion() {
            if (XCurve == null) {
                D.Err("Trying to run dolly motion while it's not loaded!");
                HasMotion = false;
                return;
            }
            var p = Progress > 1f ? 1f : Progress;
            var x = XCurve.Evaluate(p);
            var y = YCurve.Evaluate(p);
            var z = ZCurve.Evaluate(p);
            controls.TeleportTo(new(x, y, controls.PrecisePosition.z));
            controls.SetZoomDistance(z);

            if (IsPaused) { return; }

            if (Progress > 1f) {
                if (IsLoop) {
                    Progress = Progress % 1f;
                } else {
                    HasMotion = false;
                    controls.WantOrtho = controls.PreciseOrtho;
                    controls.WantPosition = controls.PrecisePosition;
                    Progress = 1f;
                }
            } else {
                var speed = (MoveSpeed / pointCount) * ZCurve.Evaluate(Progress)
                    * MoveSpeedCurve.Evaluate(Progress) * 0.1f;
                if (speed < 0.00001f) { speed = 0.00001f; }
                Progress += speed * (1f/60f);//GameTimer.deltaTime;
            }
        }

        // For demo mode mostly
        private void OnCameraDolly(Vector2 point, float dist, float speed) {
            MoveSpeed = speed;
            MoveSpeedCurve = AnimationCurve.Constant(0, 1, 1f);
            //TeleportTo(rect.Center());
            controls.Focus.FocusedPos = point;
            controls.WantPosition = Vectors.SetZ(controls.Focus.FocusedPos, controls.WantPosition.z);
            controls.WantOrtho = dist;
            controls.PlayerZooming = false;
            HasMotion = true;
            startOrtho = controls.PreciseOrtho;
            startPos = controls.PrecisePosition;
            StartDollyMotion();
        }

        public void Execute() {
            Progress = 0;
            HasMotion = true;
        }

        public void BuildCurves(int startIdx, int endIdx) {
            //var eof = new WaitForEndOfFrame();
            pointCount = endIdx - startIdx + 1;
            if (pointCount < 2) {
                D.Err("{0} >= {1}!", startIdx, endIdx);
                return;
            }

            if (IsLoop) {
                pointCount += 1;
            }

            var keysX = new Keyframe[pointCount];
            var keysY = new Keyframe[pointCount];
            var keysZ = new Keyframe[pointCount];

            var distances = new float[pointCount];

            var totalDist = 0f;
            var distI = 0;
            for (int i = startIdx; i < endIdx; i++) {
                var p = Pos(i);
                var p2 = Pos(i + 1);
                var o = Ortho(i);
                var o2 = Ortho(i + 1);
                var d = Vectors.Distance(new(p.x, p.y, o), new Vector3(p2.x, p2.y, o2));
                distances[distI++] = d;
                totalDist += d;
            }
            if (IsLoop) {
                var p = Pos(startIdx);
                var p2 = Pos(endIdx);
                var o = Ortho(startIdx);
                var o2 = Ortho(endIdx);
                var d = Vectors.Distance(new(p.x, p.y, o), new Vector3(p2.x, p2.y, o2));
                distances[distI] = d;
                totalDist += d;
            }

            var t = 0f;
            var keyI = 0;
            var distNow = 0f;
            for (int i = startIdx; i <= endIdx; i++) {
                var p = Pos(i);
                var o = Ortho(i);
                keysX[keyI] = new(t, p.x);
                keysY[keyI] = new(t, p.y);
                keysZ[keyI] = new(t, o);
                distNow += distances[keyI];
                t = distNow / totalDist;
                keyI++;
                if (t > 1f) { t = 1f; }
            }

            if (IsLoop) {
                var p = Pos(startIdx);
                var o = Ortho(startIdx);
                keysX[keyI] = new(1f, p.x);
                keysY[keyI] = new(1f, p.y);
                keysZ[keyI] = new(1f, o);
            }

            XCurve = new(keysX);
            YCurve = new(keysY);
            ZCurve = new(keysZ);
            XCurve.preWrapMode = WrapMode.PingPong;
            XCurve.postWrapMode = WrapMode.PingPong;
            YCurve.preWrapMode = WrapMode.PingPong;
            YCurve.postWrapMode = WrapMode.PingPong;
            ZCurve.preWrapMode = WrapMode.PingPong;
            ZCurve.postWrapMode = WrapMode.PingPong;

            for (var i = 0; i < pointCount; i++) {
                XCurve.SmoothTangents(i, 0);
                YCurve.SmoothTangents(i, 0);
                ZCurve.SmoothTangents(i, 0);
            }
        }
        private void StartDollyMotion(Action after = null) {
            pos = startPos;
            ortho = startOrtho;
            afterMotion = after;
            endPos = Vectors.SetZ(controls.WantPosition, controls.WantOrtho);
            endOrtho = controls.WantOrtho;
        }
    }
}
