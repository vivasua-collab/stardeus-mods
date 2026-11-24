using System;

namespace Game.Data {
    public interface IUICustomTooltipProvider {
        string ShowCustomTooltip(string currentTooltip, InfoItem item);
    }
}
