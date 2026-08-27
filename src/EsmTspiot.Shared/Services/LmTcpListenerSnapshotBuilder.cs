using System;
using System.Collections.Generic;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public static class LmTcpListenerSnapshotBuilder
    {
        public static IList<TcpListenerSnapshotItem> Build(
            IEnumerable<int> occupiedPorts,
            IList<LmServiceInventoryItem> inventory)
        {
            List<TcpListenerSnapshotItem> result = new List<TcpListenerSnapshotItem>();
            if (occupiedPorts == null)
            {
                return result;
            }

            IList<LmServiceInventoryItem> safeInventory = inventory ??
                new List<LmServiceInventoryItem>();
            foreach (int port in occupiedPorts)
            {
                string owner = FindVerifiedOwner(port, safeInventory);
                result.Add(new TcpListenerSnapshotItem(
                    port,
                    owner,
                    !string.IsNullOrEmpty(owner)));
            }
            return result;
        }

        private static string FindVerifiedOwner(
            int port,
            IList<LmServiceInventoryItem> inventory)
        {
            string owner = string.Empty;
            for (int index = 0; index < inventory.Count; index++)
            {
                LmServiceInventoryItem item = inventory[index];
                if (!IsVerifiedReadyOwner(item, port))
                {
                    continue;
                }
                if (owner.Length > 0)
                {
                    return string.Empty;
                }
                owner = item.ServiceName;
            }
            return owner;
        }

        private static bool IsVerifiedReadyOwner(LmServiceInventoryItem item, int port)
        {
            if (item == null || item.Role != LmServiceRole.Managed ||
                !item.IsRunning || !item.IsReady || item.Ports == null ||
                (item.Ports.GrpcPort != port && item.Ports.RestPort != port))
            {
                return false;
            }
            try
            {
                return string.Equals(
                    item.ServiceName,
                    LmServiceIdentity.CreateName(item.KktSerial),
                    StringComparison.Ordinal);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
