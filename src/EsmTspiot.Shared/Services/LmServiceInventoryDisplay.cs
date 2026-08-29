using System.Collections.Generic;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public sealed class LmServiceInventoryDisplay
    {
        private LmServiceInventoryDisplay()
        {
            OfficialControllers = new List<LmServiceInventoryItem>();
            InstanceServices = new List<LmServiceInventoryItem>();
        }

        public IList<LmServiceInventoryItem> OfficialControllers { get; private set; }
        public IList<LmServiceInventoryItem> InstanceServices { get; private set; }

        public static LmServiceInventoryDisplay Create(IList<LmServiceInventoryItem> inventory)
        {
            LmServiceInventoryDisplay display = new LmServiceInventoryDisplay();
            if (inventory == null)
            {
                return display;
            }
            for (int index = 0; index < inventory.Count; index++)
            {
                LmServiceInventoryItem item = inventory[index];
                if (item == null)
                {
                    continue;
                }
                if (item.Role == LmServiceRole.VerifiedOfficial)
                {
                    display.OfficialControllers.Add(item);
                }
                else
                {
                    display.InstanceServices.Add(item);
                }
            }
            return display;
        }
    }
}
