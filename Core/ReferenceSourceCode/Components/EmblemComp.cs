using System.IO;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;

namespace Game.Components {
    public sealed class EmblemComp : BaseComponent<EmblemComp>,
            IRelocatable, ICopyableComp {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new EmblemComp());
        }

        private const string EmblemReadmeContent =
@"Place custom emblem images here.
Supported formats are *.png and *.jpg.
Images must be square (1:1 aspect ratio).
Recommended resolutions are 256x256, 512x512 or 1024x1024.
You will have to restart Stardeus for new images to get detected.";

        private string emblem;
        private float objWalkDifficulty;
        private bool isWalkDiffConfigurable;
        public string Emblem => emblem;
        public bool IsWalkDiffConfigurable => isWalkDiffConfigurable;
        public float ObjWalkDifficulty => objWalkDifficulty;
        public string EmblemsPath => Persist.PersistentPath("Graphics/Emblems");

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetString("Emblem", emblem);
            data.SetFloat("WalkDiff", objWalkDifficulty);
        }

        protected override void OnLoad(ComponentData data) {
            emblem = data.GetString("Emblem", null);
            objWalkDifficulty = data.GetFloat("WalkDiff", objWalkDifficulty);
        }

        protected override void OnConfig() {
            isWalkDiffConfigurable = Config.GetBool(PropertyIdH.IsWalkDiffConfigurable, true);
            objWalkDifficulty = Config.GetFloat(PropertyIdH.WalkDifficulty, 0f);
        }

        public override string ToString() {
            return $" * Emblem [{emblem}]";
        }

        public void SetEmblem(string id) {
            emblem = id;
            Tile.Graphics.SetGraphicId(id);
        }

        public void SetWalkDifficulty(float v) {
            objWalkDifficulty = v;
            Tile.Passability.Update(objWalkDifficulty < 1f,
                objWalkDifficulty < 0.5f, objWalkDifficulty);
            EntityUtils.TriggerChange(Tile);
        }

        public void OpenEmblemsFolder() {
            S.Clock.Pause();
            var path = EmblemsPath;
            if (!Directory.Exists(path)) {
                Files.EnsureDir(path);
                Persist.Write(path, "README.txt", EmblemReadmeContent, false);
            }
            Files.OpenOSFolder(path);
        }

        public override void OnLateReady(bool wasLoaded) {
            if (emblem == null) {
                emblem = GraphicsId.EmblemsBlank;
            }
            if (emblem != Tile.Graphics.GraphicId) {
                if (Images.SpriteExists(emblem)) {
                    Tile.Graphics.SetGraphicId(emblem);
                } else {
                    #if !PRODUCTION_BUILD
                    D.Log("Missing emblem: {0}", emblem);
                    #else
                    D.Warn("Missing emblem: {0}", emblem);
                    #endif
                    emblem = GraphicsId.EmblemsBlank;
                }
            }
            if (isWalkDiffConfigurable && wasLoaded) {
                SetWalkDifficulty(objWalkDifficulty);
            }
        }

        public bool IsCopyable => true;
        public string CopyConfigText => T.CopyEmblem;

        public void RelocateTo(Entity target) {
            var emb = target.GetComponent<EmblemComp>();
            emb.emblem = emblem;
            emb.Tile.Graphics.SetGraphicId(emblem);
        }

        public bool CopyConfigTo(ICopyableComp target) {
            bool copied = false;
            if (target is EmblemComp other) {
                if (other.emblem != emblem) {
                    other.emblem = emblem;
                    other.Tile.Graphics.SetGraphicId(Tile.Graphics.GraphicId);
                    copied = true;
                }
                if (isWalkDiffConfigurable && other.isWalkDiffConfigurable) {
                    if (!Maths.Approximately(other.objWalkDifficulty, objWalkDifficulty)) {
                        other.SetWalkDifficulty(objWalkDifficulty);
                        copied = true;
                    }
                }
            }
            return copied;
        }

        public bool CanCopyTo(ICopyableComp comp) {
            return comp is EmblemComp m
                && m.Tile.Transform.Width == Tile.Transform.Width;
        }
    }
}
