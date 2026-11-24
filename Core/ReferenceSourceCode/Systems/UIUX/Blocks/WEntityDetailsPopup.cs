using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Data;
using Game.Utils;
using KL.Pool;
using KL.Utils;

namespace Game.Systems.UIUX {
    /// <summary>
    /// FIXME this isn't used, and the implementation is retarded.
    /// </summary>
    public sealed class WEntityDetailsPopup : WBase, ICustomPoolable, ICloseableWidget {
        public WEntityHeader Header;
        public WButton CloseButton;
        public WGroup Content;
        public WGroup Footer;
        private Lifetime lifetime;
        private readonly EntityOverviewContext ctx = new();

        private void Awake() {
            CloseButton.Load(Dispose);
        }

        public void Load(BeingData being) {
            if (being == null) {
                return;
            }

            lifetime?.Dispose();
            lifetime = Lifetime.Create($"WEntityDetailsPopup_{being.Id}");

            Header.Load((string) null, being.Mind.Name.FullNameOrNameless);
            Header.SetText2(being.UI.GetBio(false, true));
            Header.SetFactionIcon(being.EffectiveFaction);
            Header.AutoResize();
            Header.SetOnClick(() => {
                if (being.Being != null) {
                    EntityUtils.CameraFocusOn(being.Being);
                }
            });
            being.Avatar.Load(Header.Image);

            var persona = Registries.EntityOverviewBlocks.Get(
                EntityOverview_BeingPersona.BlockId, errorIfMissing: false)
                ?? new EntityOverview_BeingPersona();
            ctx.Being = being;
            ctx.Entity = being.Being;
            ctx.Lifetime = lifetime;
            int idx = 0;
            persona.LoadContent(ctx, Content, ref idx);
            ctx.Reset();
        }

        public void PoolActivate() {
            Footer.SetActiveFast(false);
            gameObject.SetActiveFast(true);
        }

        public void PoolDeactivate() {
            lifetime?.Dispose();
            lifetime = null;
            ctx.Reset();
            gameObject.SetActiveFast(false);
        }

        public bool TryCloseWidget() {
            Dispose();
            return true;
        }
    }
}
