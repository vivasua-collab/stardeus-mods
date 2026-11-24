using UnityEngine;

namespace Game.Utils {
    [CreateAssetMenu(fileName = "LinkedResources", menuName = "ScriptableObjects/LinkedResources", order = 1)]
    public sealed class LinkedResources : ScriptableObject {
        private static LinkedResources _instance;
        public static LinkedResources Instance =>
            _instance ??= Resources.Load<LinkedResources>("SO/LinkedResources");

        public Sprite SpaceMapLinkSprite;
        public Sprite SpaceMapLinkIsolatedSprite;
        public Mesh QuadMesh;
        public Texture2D OutPortTex;
        public Texture2D InPortTex;
        public Texture2D WorkSpotTex;
        public Texture2D ThoughtBubbleTex;
        public Texture2D InfoBarFillTex;
        public Texture2D InfoBarBGTex;
        public Texture2D HyperjumpQueueLineTex;
        public Sprite WarningSprite;
        public Sprite LogoSteam;
        public Sprite LogoDiscord;
        public Sprite LogoTwitch;
        public Sprite LogoX;
        public Sprite IconLanguagesA;
        public Sprite IconLanguagesB;
    }
}
