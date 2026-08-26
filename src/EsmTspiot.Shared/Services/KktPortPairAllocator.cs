using System.Collections.Generic;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public sealed class KktPortPairAllocator
    {
        private const int PortBase = 50400;
        private const int SoftPortBase = 51400;
        private const int MaximumPairIndex = 1000;
        private readonly HashSet<int> _occupiedIndexes = new HashSet<int>();

        public KktPortPairAllocator(IList<KktInstanceInfo> instances)
        {
            if (instances == null)
            {
                return;
            }

            for (int i = 0; i < instances.Count; i++)
            {
                KktInstanceInfo instance = instances[i];
                if (instance == null)
                {
                    continue;
                }

                ReservePairIndex(instance.Port, PortBase);
                ReservePairIndex(instance.SoftPort, SoftPortBase);
            }
        }

        public KktPortPair ReserveNext()
        {
            for (int index = 1; index <= MaximumPairIndex; index++)
            {
                if (_occupiedIndexes.Contains(index))
                {
                    continue;
                }

                _occupiedIndexes.Add(index);
                return new KktPortPair
                {
                    Index = index,
                    Port = (PortBase + index).ToString(),
                    SoftPort = (SoftPortBase + index).ToString()
                };
            }

            return null;
        }

        private void ReservePairIndex(string value, int portBase)
        {
            int port;
            if (!int.TryParse(value, out port))
            {
                return;
            }

            int index = port - portBase;
            if (index >= 1 && index <= MaximumPairIndex)
            {
                _occupiedIndexes.Add(index);
            }
        }
    }
}
